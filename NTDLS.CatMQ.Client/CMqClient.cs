using NTDLS.CatMQ.Client.Client.QueryHandlers;
using NTDLS.CatMQ.Shared;
using NTDLS.CatMQ.Shared.Payload.ClientToServer;
using NTDLS.ReliableMessaging;
using NTDLS.Semaphore;
using System.Net;
using System.Text.Json;

namespace NTDLS.CatMQ.Client
{
    /// <summary>
    /// Connects to a MessageServer then sends/received and processes notifications/queries.
    /// </summary>
    public class CMqClient
        : IDisposable
    {
        private readonly RmClient _rmClient;
        private readonly CMqClientConfiguration _configuration;

        /// <summary>
        /// Contains the queue name and the handler delegate function for that queue.
        /// </summary>
        private readonly OptimisticCriticalResource<QueueSubscriptionDictionary> _subscriptions = new();
        private readonly OptimisticCriticalResource<MessageBufferDictionary> _messageBuffer = new();
        private volatile Task? _bufferedDeliveryTask;

        /// <summary>
        /// Provides access to the custom serialization provider, if configured.
        /// </summary>
        public ICMqSerializationProvider? SerializationProvider { get; private set; }

        /// <summary>
        /// Returns true if the client is connected.
        /// </summary>
        public bool IsConnected => _rmClient.IsConnected;

        /// <summary>
        /// Delegate used for client connectivity notifications.
        /// </summary>
        /// <param name="client">The client instance.</param>
        /// <param name="isReconnect">True when the connection was an auto-reconnect, otherwise false.</param>
        public delegate void OnConnectedEvent(CMqClient client, bool isReconnect);

        /// <summary>
        /// Delegate used for client (dis)connectivity notifications.
        /// </summary>
        /// <param name="client">The client instance.</param>
        public delegate void OnDisconnectedEvent(CMqClient client);

        /// <summary>
        /// Event used client connectivity notifications.
        /// </summary>
        public event OnConnectedEvent? OnConnected;

        /// <summary>
        /// Event used client connectivity notifications.
        /// </summary>
        public event OnDisconnectedEvent? OnDisconnected;

        /// <summary>
        /// Delegate used to notify of queue client exceptions.
        /// </summary>
        public delegate void OnExceptionEvent(CMqClient client, string? queueName, Exception ex);

        /// <summary>
        /// Event used to notify of queue client exceptions.
        /// </summary>
        public event OnExceptionEvent? OnException;

        /// <summary>
        /// Creates a new instance of the queue service.
        /// </summary>
        public CMqClient(CMqClientConfiguration configuration)
        {
            _configuration = configuration;

            var rmConfiguration = new RmConfiguration()
            {
                InitialReceiveBufferSize = _configuration.InitialReceiveBufferSize,
                MaxReceiveBufferSize = _configuration.MaxReceiveBufferSize,
                QueryTimeout = _configuration.QueryTimeout,
                ReceiveBufferGrowthRate = _configuration.ReceiveBufferGrowthRate,
                CompressionProvider = new RmDeflateCompressionProvider(),
                AutoReconnect = _configuration.AutoReconnect,
            };

            _rmClient = new RmClient(rmConfiguration);
            _rmClient.OnConnected += RmClient_OnConnected;
            _rmClient.OnDisconnected += RmClient_OnDisconnected;
            _rmClient.AddHandler(new InternalClientQueryHandlers(this));
        }

        /// <summary>
        /// Creates a new instance of the queue client.
        /// </summary>
        public CMqClient()
        {
            _configuration = new CMqClientConfiguration();

            var rmConfiguration = new RmConfiguration()
            {
                InitialReceiveBufferSize = _configuration.InitialReceiveBufferSize,
                MaxReceiveBufferSize = _configuration.MaxReceiveBufferSize,
                QueryTimeout = _configuration.QueryTimeout,
                ReceiveBufferGrowthRate = _configuration.ReceiveBufferGrowthRate,
                CompressionProvider = new RmDeflateCompressionProvider(),
                AutoReconnect = _configuration.AutoReconnect,
            };

            _rmClient = new RmClient(rmConfiguration);
            _rmClient.OnConnected += RmClient_OnConnected;
            _rmClient.OnDisconnected += RmClient_OnDisconnected;
            _rmClient.AddHandler(new InternalClientQueryHandlers(this));
        }

        /// <summary>
        /// Sets the custom serialization provider.
        /// Can be cleared by passing null or calling ClearCryptographyProvider().
        /// </summary>
        public void SetSerializationProvider(ICMqSerializationProvider? provider)
        {
            SerializationProvider = provider;
        }

        /// <summary>
        /// Removes the serialization provider set by a previous call to SetSerializationProvider().
        /// </summary>
        public void ClearSerializationProvider()
        {
            SerializationProvider = null;
        }

        private void RegisterSubscription(CMqSubscription subscription)
        {
            _subscriptions.Write(s =>
            {
                if (s.ContainsKey(subscription.QueueName))
                {
                    throw new Exception($"Client is already subscribed to queue [{subscription.QueueName}].");
                }

                s[subscription.QueueName] = subscription;
            });
        }

        /// <summary>
        /// Removes a subscription that the server did not accept, so the caller can retry.
        /// </summary>
        private void DiscardSubscription(CMqSubscription subscription)
        {
            _subscriptions.Write(s =>
            {
                if (s.TryGetValue(subscription.QueueName, out var existing) && existing.Id == subscription.Id)
                {
                    s.Remove(subscription.QueueName);
                }
            });
        }

        private void RmClient_OnConnected(RmContext context, bool isReconnect)
        {
            if (isReconnect)
            {
                //Subscriptions are tied to the server-side connection, so they are lost when the connection drops.
                //Run on another thread so we do not issue queries from inside the connection callback.
                Task.Run(RestoreSubscriptions);
            }

            OnConnected?.Invoke(this, isReconnect);
        }

        private void RestoreSubscriptions()
        {
            try
            {
                var queueNames = _subscriptions.Read(s => s.Values.Select(o => o.QueueName).ToList());

                foreach (var queueName in queueNames)
                {
                    try
                    {
                        _rmClient.Query(new CMqSubscribeToQueueQuery(queueName)).ThrowIfFailed();
                    }
                    catch (Exception ex)
                    {
                        OnException?.Invoke(this, queueName, ex.GetBaseException());
                    }
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, null, ex.GetBaseException());
            }
        }

        private void RmClient_OnDisconnected(RmContext context)
            => OnDisconnected?.Invoke(this);

        internal CMqConsumeResult InvokeOnReceived(CMqClient client, CMqReceivedMessage message)
        {
            var subscriptionHandler = _subscriptions.Read(s =>
            {
                s.TryGetValue(message.QueueName, out var handler);
                return handler;
            });

            if (subscriptionHandler != null)
            {
                //If a custom serialization provider is configured, enrich the message with it so that it can be used for unboxing.
                message.SerializationProvider = SerializationProvider;

                //If the message is buffered, add it to the buffer and return consumed.
                if (subscriptionHandler.BatchSize != null && subscriptionHandler.BatchDeliveryEvent != null)
                {
                    _messageBuffer.Write(mb =>
                    {
                        if (mb.TryGetValue(subscriptionHandler.Id, out var buffer))
                        {
                            buffer.Add(message);
                        }
                        else
                        {
                            mb.Add(subscriptionHandler.Id, [message]);
                        }
                    });

                    return new CMqConsumeResult(CMqConsumptionDisposition.Consumed);
                }
                //If the message is not buffered, invoke the delivery event and return the result to the server.
                else if (subscriptionHandler.DeliveryEvent != null)
                {
                    return subscriptionHandler.DeliveryEvent.Invoke(client, message);
                }
            }

            return new CMqConsumeResult(CMqConsumptionDisposition.NotInterested);
        }

        internal void InvokeOnException(CMqClient client, string? queueName, Exception ex)
            => OnException?.Invoke(client, queueName, ex);

        /// <summary>
        /// Connects the client to a queue server.
        /// </summary>
        public void Connect(string hostName, int port)
        {
            ResetBufferedMessagesAndSubscriptions();

            _rmClient.Connect(hostName, port);
        }

        /// <summary>
        /// Connects the client to a queue server.
        /// </summary>
        public void Connect(IPAddress ipAddress, int port)
        {
            ResetBufferedMessagesAndSubscriptions();
            _rmClient.Connect(ipAddress, port);
        }

        /// <summary>
        /// Disconnects the client from the queue server.
        /// </summary>
        public void Disconnect(bool wait = false)
        {
            _rmClient.Disconnect(wait);
            _bufferedDeliveryTask?.Wait();
        }

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public async Task CreateQueueAsync(string queueName)
            => (await _rmClient.QueryAsync(new CMqCreateQueueQuery(new CMqQueueConfiguration(queueName)))).ThrowIfFailed();

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public async Task CreateQueueAsync(CMqQueueConfiguration queueConfiguration)
            => (await _rmClient.QueryAsync(new CMqCreateQueueQuery(queueConfiguration))).ThrowIfFailed();

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public void CreateQueue(string queueName)
            => _rmClient.Query(new CMqCreateQueueQuery(new CMqQueueConfiguration(queueName))).ThrowIfFailed();

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public void CreateQueue(CMqQueueConfiguration queueConfiguration)
            => _rmClient.Query(new CMqCreateQueueQuery(queueConfiguration)).ThrowIfFailed();

        /// <summary>
        /// Instructs the server to delete the queue with the given name.
        /// </summary>
        public async Task DeleteQueueAsync(string queueName)
            => (await _rmClient.QueryAsync(new CMqDeleteQueueQuery(queueName))).ThrowIfFailed();

        /// <summary>
        /// Instructs the server to delete the queue with the given name.
        /// </summary>
        public void DeleteQueue(string queueName)
            => _rmClient.Query(new CMqDeleteQueueQuery(queueName)).ThrowIfFailed();

        /// <summary>
        /// Instructs the server to remove all messages from the queue with the given name.
        /// </summary>
        public async Task PurgeQueueAsync(string queueName)
            => (await _rmClient.QueryAsync(new CMqPurgeQueueQuery(queueName))).ThrowIfFailed();

        /// <summary>
        /// Instructs the server to remove all messages from the queue with the given name.
        /// </summary>
        public void PurgeQueue(string queueName)
            => _rmClient.Query(new CMqPurgeQueueQuery(queueName)).ThrowIfFailed();

        /// <summary>
        /// Instructs the server to notify the client of messages sent to the given queue.
        /// </summary>
        /// <param name="queueName">Queue name to subscribe to.</param>
        /// <param name="deliveryEvent">Delegate function to call for each message.</param>
        public async Task<CMqSubscription> SubscribeAsync(string queueName, OnMessageReceived deliveryEvent)
        {
            var subscription = new CMqSubscription(queueName, deliveryEvent);
            RegisterSubscription(subscription);

            try
            {
                (await _rmClient.QueryAsync(new CMqSubscribeToQueueQuery(queueName))).ThrowIfFailed();
            }
            catch
            {
                DiscardSubscription(subscription);
                throw;
            }

            return subscription;
        }

        /// <summary>
        /// Instructs the server to notify the client of messages sent to the given queue.
        /// </summary>
        /// <param name="queueName">Queue name to subscribe to.</param>
        /// <param name="deliveryEvent">Delegate function to call for each message.</param>
        public CMqSubscription Subscribe(string queueName, OnMessageReceived deliveryEvent)
        {
            var subscription = new CMqSubscription(queueName, deliveryEvent);
            RegisterSubscription(subscription);

            try
            {
                _rmClient.Query(new CMqSubscribeToQueueQuery(queueName)).ThrowIfFailed();
            }
            catch
            {
                DiscardSubscription(subscription);
                throw;
            }

            return subscription;
        }

        #region Client-side message buffering.

        /// <summary>
        /// Instructs the server to notify the client of messages sent to the given queue.
        /// The messages are buffered at the client until the batch size is met or the auto flush interval is reached.
        /// Note that buffered subscriptions fo not allow for delivery dispositions to be returned to the server and all delivered messages to this subscription will be considered consumed.
        /// </summary>
        /// <param name="queueName">Queue name to subscribe to.</param>
        /// <param name="batchSize">The number of messages to present to the subscriber event in each batch.</param>
        /// <param name="autoFlushInterval">The amount of time to wait before presenting the messages to the subscriber even when the batchSize is not met. (0 = never)</param>
        /// <param name="batchDeliveryEvent">Delegate function to call for each batch.</param>
        public async Task<CMqSubscription> SubscribeBufferedAsync(string queueName, int batchSize, TimeSpan autoFlushInterval, OnBatchReceived batchDeliveryEvent)
        {
            var subscription = new CMqSubscription(queueName, batchSize, autoFlushInterval, batchDeliveryEvent);
            RegisterSubscription(subscription);

            try
            {
                (await _rmClient.QueryAsync(new CMqSubscribeToQueueQuery(queueName))).ThrowIfFailed();
            }
            catch
            {
                DiscardSubscription(subscription);
                throw;
            }

            StartBufferedDeliveryTask();

            return subscription;
        }

        /// <summary>
        /// Instructs the server to notify the client of messages sent to the given queue.
        /// The messages are buffered at the client until the batch size is met or the auto flush interval is reached.
        /// Note that buffered subscriptions fo not allow for delivery dispositions to be returned to the server and all delivered messages to this subscription will be considered consumed.
        /// </summary>
        /// <param name="queueName">Queue name to subscribe to.</param>
        /// <param name="batchSize">The number of messages to present to the subscriber event in each batch.</param>
        /// <param name="autoFlushInterval">The amount of time to wait before presenting the messages to the subscriber even when the batchSize is not met. (0 = never)</param>
        /// <param name="batchDeliveryEvent">Delegate function to call for each batch.</param>
        public CMqSubscription SubscribeBuffered(string queueName, int batchSize, TimeSpan autoFlushInterval, OnBatchReceived batchDeliveryEvent)
        {
            var subscription = new CMqSubscription(queueName, batchSize, autoFlushInterval, batchDeliveryEvent);
            RegisterSubscription(subscription);

            try
            {
                _rmClient.Query(new CMqSubscribeToQueueQuery(queueName)).ThrowIfFailed();
            }
            catch
            {
                DiscardSubscription(subscription);
                throw;
            }

            StartBufferedDeliveryTask();

            return subscription;
        }

        private void StartBufferedDeliveryTask()
        {
            try
            {
                //The task exits when the client is explicitly disconnected, so restart it if the client has since been reconnected.
                if (_bufferedDeliveryTask == null || _bufferedDeliveryTask.IsCompleted)
                {
                    lock (this)
                    {
                        if (_bufferedDeliveryTask == null || _bufferedDeliveryTask.IsCompleted)
                        {
                            _bufferedDeliveryTask = Task.Run(BufferedDeliveryTaskProc);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, null, ex.GetBaseException());
            }
        }

        private void BufferedDeliveryTaskProc()
        {
            try
            {
                while (!_rmClient.ExplicitlyDisconnected)
                {
                    FlushBufferedMessages(false);
                    Thread.Sleep(1);
                }

                FlushBufferedMessages(true);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, null, ex.GetBaseException());
            }
        }

        private void ResetBufferedMessagesAndSubscriptions()
        {
            try
            {
                bool success;

                do
                {
                    success = true;

                    success = _messageBuffer.TryWrite(mb =>
                    {
                        success = _subscriptions.TryWrite(s =>
                        {
                            s.Clear();
                            mb.Clear();
                        }) && success;
                    }) && success;
                } while (!success);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, null, ex.GetBaseException());
            }
        }

        /// <summary>
        /// Flushes the message buffers to the subscribed batch events.
        /// </summary>
        /// <param name="force">When true, every buffered message is delivered regardless of batch size or flush interval,
        /// and the call does not return until the buffers have been flushed. We do this because we *typically*
        /// allow failure-to-lock to defer the event.</param>
        private void FlushBufferedMessages(bool force)
        {
            try
            {
                bool success;

                do
                {
                    success = true;

                    success = _messageBuffer.TryWrite(mb =>
                    {
                        if (mb.Count == 0)
                        {
                            return;
                        }

                        success = _subscriptions.TryRead(s =>
                        {
                            foreach (var subscription in s.Values)
                            {
                                //Each subscription only ever flushes its own buffer.
                                if (subscription.BatchSize == null || !mb.TryGetValue(subscription.Id, out var buffered))
                                {
                                    continue;
                                }

                                var now = DateTime.UtcNow;

                                if (buffered.Count == 0)
                                {
                                    subscription.LastBufferFlushed = now;
                                    continue;
                                }

                                if (force
                                    || buffered.Count >= subscription.BatchSize
                                    || (subscription.AutoFlushInterval != TimeSpan.Zero
                                        && (now - subscription.LastBufferFlushed) >= subscription.AutoFlushInterval))
                                {
                                    var batch = buffered.ToList();
                                    buffered.Clear();
                                    subscription.LastBufferFlushed = now;

                                    void DeliverBatch()
                                    {
                                        try
                                        {
                                            subscription.BatchDeliveryEvent?.Invoke(this, batch);
                                        }
                                        catch (Exception ex)
                                        {
                                            OnException?.Invoke(this, subscription.QueueName, ex.GetBaseException());
                                        }
                                    }

                                    if (force)
                                    {
                                        DeliverBatch(); //We are shutting down, so deliver before returning.
                                    }
                                    else
                                    {
                                        Task.Run(DeliverBatch);
                                    }
                                }
                            }
                        }) && success;
                    }) && success;

                    if (!success && force)
                    {
                        Thread.Sleep(1);
                    }
                } while (!success && force);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, null, ex.GetBaseException());
            }
        }

        #endregion

        /// <summary>
        /// Instructs the server to stop notifying the client of messages sent to the given queue.
        /// </summary>
        public async Task UnsubscribeAsync(string queueName)
        {
            var existingSubscription = _subscriptions.Read(s => s.ContainsKey(queueName));
            if (existingSubscription == false)
            {
                throw new Exception($"Client is not subscribed to queue [{queueName}].");
            }

            (await _rmClient.QueryAsync(new CMqUnsubscribeFromQueueQuery(queueName))).ThrowIfFailed();

            _subscriptions.Write(s => s.Remove(queueName, out var existingSubscription));
        }

        /// <summary>
        /// Instructs the server to stop notifying the client of messages sent to the given queue.
        /// </summary>
        public void Unsubscribe(string queueName)
        {
            var existingSubscription = _subscriptions.Read(s => s.ContainsKey(queueName));
            if (existingSubscription == false)
            {
                throw new Exception($"Client is not subscribed to queue [{queueName}].");
            }

            _rmClient.Query(new CMqUnsubscribeFromQueueQuery(queueName)).ThrowIfFailed();

            _subscriptions.Write(s => s.Remove(queueName, out var existingSubscription));
        }

        /// <summary>
        /// Dispatches a message to the queue server to be enqueued in the given queue.
        /// Waits on a confirmation response from the server before returning and throws if the server indicates a failure to enqueue the message.
        /// </summary>
        /// <typeparam name="T">Type of the payload contained in the message </typeparam>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="message">Payload message inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public async Task EnqueueAsync<T>(string queueName, T message, CMqEnqueueOptions? options = null)
            where T : ICMqMessage
        {
            string? messageJson;
            if (SerializationProvider != null)
            {
                messageJson = SerializationProvider.SerializeToText(message);
            }
            else
            {
                messageJson = JsonSerializer.Serialize((object)message);
            }

            var objectType = CMqSerialization.GetAssemblyQualifiedTypeName(message);

            (await _rmClient.QueryAsync(new CMqEnqueueMessageToQueueWithConfirmation(queueName, options?.DeferDeliveryDuration,
                objectType, messageJson), options?.ServerDeliveryTimeout)).ThrowIfFailed();
        }

        /// <summary>
        /// Dispatches a pre-serialized message to the queue server to be enqueued in the given queue.
        /// Waits on a confirmation response from the server before returning and throws if the server indicates a failure to enqueue the message.
        /// </summary>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="assemblyQualifiedName">Fully assembly qualified type of the message type for deserialization.</param>
        /// <param name="messageJson">Json for payload message of type inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public async Task EnqueueAsync(string queueName, string assemblyQualifiedName, string messageJson, CMqEnqueueOptions? options = null)
        {
            (await _rmClient.QueryAsync(new CMqEnqueueMessageToQueueWithConfirmation(queueName, options?.DeferDeliveryDuration,
                assemblyQualifiedName, messageJson), options?.ServerDeliveryTimeout)).ThrowIfFailed();
        }

        /// <summary>
        /// Dispatches a message to the queue server to be enqueued in the given queue.
        /// Waits on a confirmation response from the server before returning and throws if the server indicates a failure to enqueue the message.
        /// </summary>
        /// <typeparam name="T">Type of the payload contained in the message </typeparam>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="message">Payload message inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public void Enqueue<T>(string queueName, T message, CMqEnqueueOptions? options = null)
            where T : ICMqMessage
        {
            string? messageJson;
            if (SerializationProvider != null)
            {
                messageJson = SerializationProvider.SerializeToText(message);
            }
            else
            {
                messageJson = JsonSerializer.Serialize((object)message);
            }

            var objectType = CMqSerialization.GetAssemblyQualifiedTypeName(message);

            _rmClient.Query(new CMqEnqueueMessageToQueueWithConfirmation(queueName, options?.DeferDeliveryDuration,
                objectType, messageJson), options?.ServerDeliveryTimeout).ThrowIfFailed();
        }

        /// <summary>
        /// Dispatches a pre-serialized message to the queue server to be enqueued in the given queue.
        /// Waits on a confirmation response from the server before returning and throws if the server indicates a failure to enqueue the message.
        /// </summary>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="assemblyQualifiedName">Fully assembly qualified type of the message type for deserialization.</param>
        /// <param name="messageJson">Json for payload message of type inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public void Enqueue(string queueName, string assemblyQualifiedName, string messageJson, CMqEnqueueOptions? options = null)
            => _rmClient.Query(new CMqEnqueueMessageToQueueWithConfirmation(queueName, options?.DeferDeliveryDuration,
                assemblyQualifiedName, messageJson), options?.ServerDeliveryTimeout).ThrowIfFailed();

        /// <summary>
        /// Dispatches a message to the queue server to be enqueued in the given queue.
        /// Does not wait on a confirmation response from the server before returning which
        ///     is very efficient but does not provide any guarantee that the message was successfully enqueued.
        /// </summary>
        /// <typeparam name="T">Type of the payload contained in the message </typeparam>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="message">Payload message inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public async Task EnqueueExpedientAsync<T>(string queueName, T message, CMqEnqueueOptions? options = null)
            where T : ICMqMessage
        {
            string? messageJson;
            if (SerializationProvider != null)
            {
                messageJson = SerializationProvider.SerializeToText(message);
            }
            else
            {
                messageJson = JsonSerializer.Serialize((object)message);
            }

            var objectType = CMqSerialization.GetAssemblyQualifiedTypeName(message);
            await _rmClient.NotifyAsync(new CMqEnqueueMessageToQueueExpedient(queueName, options?.DeferDeliveryDuration, objectType, messageJson));
        }

        /// <summary>
        /// Dispatches a pre-serialized message to the queue server to be enqueued in the given queue.
        /// Does not wait on a confirmation response from the server before returning which
        ///     is very efficient but does not provide any guarantee that the message was successfully enqueued.
        /// </summary>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="assemblyQualifiedName">Fully assembly qualified type of the message type for deserialization.</param>
        /// <param name="messageJson">Json for payload message of type inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public async Task EnqueueExpedientAsync(string queueName, string assemblyQualifiedName, string messageJson, CMqEnqueueOptions? options = null)
        {
            await _rmClient.NotifyAsync(new CMqEnqueueMessageToQueueExpedient(queueName, options?.DeferDeliveryDuration, assemblyQualifiedName, messageJson));
        }

        /// <summary>
        /// Dispatches a message to the queue server to be enqueued in the given queue.
        /// Does not wait on a confirmation response from the server before returning which
        ///     is very efficient but does not provide any guarantee that the message was successfully enqueued.
        /// </summary>
        /// <typeparam name="T">Type of the payload contained in the message </typeparam>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="message">Payload message inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public void EnqueueExpedient<T>(string queueName, T message, CMqEnqueueOptions? options = null)
            where T : ICMqMessage
        {
            string? messageJson;
            if (SerializationProvider != null)
            {
                messageJson = SerializationProvider.SerializeToText(message);
            }
            else
            {
                messageJson = JsonSerializer.Serialize((object)message);
            }

            var objectType = CMqSerialization.GetAssemblyQualifiedTypeName(message);

            _rmClient.Notify(new CMqEnqueueMessageToQueueExpedient(queueName, options?.DeferDeliveryDuration, objectType, messageJson));
        }

        /// <summary>
        /// Dispatches a pre-serialized message to the queue server to be enqueued in the given queue.
        /// Does not wait on a confirmation response from the server before returning which
        ///     is very efficient but does not provide any guarantee that the message was successfully enqueued.
        /// </summary>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="assemblyQualifiedName">Fully assembly qualified type of the message type for deserialization.</param>
        /// <param name="messageJson">Json for payload message of type inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public void EnqueueExpedient(string queueName, string assemblyQualifiedName, string messageJson, CMqEnqueueOptions? options = null)
            => _rmClient.Notify(new CMqEnqueueMessageToQueueExpedient(queueName, options?.DeferDeliveryDuration, assemblyQualifiedName, messageJson));

        /// <summary>
        /// Disconnects the client from the queue server.
        /// This does not need to be called if Disconnect() is called.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Disconnect();
        }
    }
}
