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
        private bool _explicitDisconnect = false;
        private readonly CMqClientConfiguration _configuration;

        /// <summary>
        /// Contains the queue name and the handler delegate function for that queue.
        /// </summary>
        private readonly OptimisticCriticalResource<QueueSubscriptionDictionary> _subscriptions = new();
        private readonly OptimisticCriticalResource<MessageBufferDictionary> _messageBuffer = new();
        private Task? _bufferedDeliveryTask;

        /// <summary>
        /// Provides access to the custom serialization provider, if configured.
        /// </summary>
        public ICMqSerializationProvider? SerializationProvider { get; private set; }

        private string? _lastReconnectHost;
        private int _lastReconnectPort;
        private IPAddress? _lastReconnectIpAddress;

        /// <summary>
        /// Returns true if the client is connected.
        /// </summary>
        public bool IsConnected => _rmClient.IsConnected;

        /// <summary>
        /// Event used for server-to-client delivery notifications containing raw JSON.
        /// </summary>
        public delegate void OnConnectedEvent(CMqClient client);

        /// <summary>
        /// Event used client connectivity notifications.
        /// </summary>
        public event OnConnectedEvent? OnConnected;

        /// <summary>
        /// Event used client connectivity notifications.
        /// </summary>
        public event OnConnectedEvent? OnDisconnected;

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
                CompressionProvider = new RmDeflateCompressionProvider()
            };

            _rmClient = new RmClient(rmConfiguration);
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
                CompressionProvider = new RmDeflateCompressionProvider()
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

        private void RmClient_OnConnected(RmContext context)
        {
            _explicitDisconnect = false;
            OnConnected?.Invoke(this);
        }

        private void RmClient_OnDisconnected(RmContext context)
        {
            try
            {
                OnDisconnected?.Invoke(this);

                if (!_explicitDisconnect && _configuration.AutoReconnect)
                {
                    _ = Task.Run(() =>
                    {
                        while (!_explicitDisconnect && !_rmClient.IsConnected)
                        {
                            try
                            {
                                if (_lastReconnectHost != null)
                                {
                                    Connect(_lastReconnectHost, _lastReconnectPort);
                                }
                                else if (_lastReconnectIpAddress != null)
                                {
                                    Connect(_lastReconnectIpAddress, _lastReconnectPort);
                                }
                                else
                                {
                                    throw new Exception("Cannot reconnect because no previous connection information is available.");
                                }
                            }
                            catch (Exception ex)
                            {
                                OnException?.Invoke(this, null, ex.GetBaseException());
                            }

                            Thread.Sleep(1000);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, null, ex.GetBaseException());
            }
        }

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
                            mb.Add(subscriptionHandler.Id, new List<CMqReceivedMessage> { message });
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

            _lastReconnectHost = hostName;
            _lastReconnectIpAddress = null;
            _lastReconnectPort = port;

            _explicitDisconnect = false;

            _rmClient.Connect(hostName, port);
        }

        /// <summary>
        /// Connects the client to a queue server.
        /// </summary>
        public void Connect(IPAddress ipAddress, int port)
        {
            ResetBufferedMessagesAndSubscriptions();

            _lastReconnectHost = null;
            _lastReconnectIpAddress = ipAddress;
            _lastReconnectPort = port;

            _explicitDisconnect = false;

            _rmClient.Connect(ipAddress, port);
        }

        /// <summary>
        /// Connects the client to a queue server in a background thread.
        /// </summary>
        public void ConnectBackground(string hostName, int port)
        {
            _ = Task.Run(() =>
            {
                while (!_explicitDisconnect)
                {
                    try
                    {
                        Connect(hostName, port);
                        return;
                    }
                    catch
                    {
                        if (_configuration.AutoReconnect == false)
                        {
                            return;
                        }
                    }
                    Thread.Sleep(500);
                }
            });
        }

        /// <summary>
        /// Connects the client to a queue server in a background thread.
        /// </summary>
        public void ConnectBackground(IPAddress ipAddress, int port)
        {
            _ = Task.Run(() =>
            {
                while (!_explicitDisconnect)
                {
                    try
                    {
                        Connect(ipAddress, port);
                        return;
                    }
                    catch
                    {
                        if (_configuration.AutoReconnect == false)
                        {
                            return;
                        }
                    }
                    Thread.Sleep(500);
                }
            });
        }

        /// <summary>
        /// Disconnects the client from the queue server.
        /// </summary>
        public void Disconnect(bool wait = false)
        {
            _explicitDisconnect = true;
            _rmClient.Disconnect(wait);
            _bufferedDeliveryTask?.Wait();
        }

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public async Task CreateQueueAsync(string queueName)
        {
            var result = await _rmClient.QueryAsync(new CMqCreateQueueQuery(new CMqQueueConfiguration(queueName)));
            result.ThrowIfFailed();
        }

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public async Task CreateQueueAsync(CMqQueueConfiguration queueConfiguration)
        {
            var result = await _rmClient.QueryAsync(new CMqCreateQueueQuery(queueConfiguration));
            result.ThrowIfFailed();
        }

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public void CreateQueue(string queueName)
        {
            var result = _rmClient.Query(new CMqCreateQueueQuery(new CMqQueueConfiguration(queueName))).Result;
            result.ThrowIfFailed();
        }

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public void CreateQueue(CMqQueueConfiguration queueConfiguration)
        {
            var result = _rmClient.Query(new CMqCreateQueueQuery(queueConfiguration)).Result;
            result.ThrowIfFailed();
        }

        /// <summary>
        /// Instructs the server to delete the queue with the given name.
        /// </summary>
        public async Task DeleteQueueAsync(string queueName)
        {
            var result = await _rmClient.QueryAsync(new CMqDeleteQueueQuery(queueName));
            result.ThrowIfFailed();
        }

        /// <summary>
        /// Instructs the server to delete the queue with the given name.
        /// </summary>
        public void DeleteQueue(string queueName)
        {
            var result = _rmClient.Query(new CMqDeleteQueueQuery(queueName)).Result;
            result.ThrowIfFailed();
        }

        /// <summary>
        /// Instructs the server to remove all messages from the queue with the given name.
        /// </summary>
        public async Task PurgeQueueAsync(string queueName)
        {
            var result = await _rmClient.QueryAsync(new CMqPurgeQueueQuery(queueName));
            result.ThrowIfFailed();
        }

        /// <summary>
        /// Instructs the server to remove all messages from the queue with the given name.
        /// </summary>
        public void PurgeQueue(string queueName)
        {
            var result = _rmClient.Query(new CMqPurgeQueueQuery(queueName)).Result;
            result.ThrowIfFailed();
        }

        /// <summary>
        /// Instructs the server to notify the client of messages sent to the given queue.
        /// </summary>
        /// <param name="queueName">Queue name to subscribe to.</param>
        /// <param name="deliveryEvent">Delegate function to call for each message.</param>
        public async Task<CMqSubscription> SubscribeAsync(string queueName, OnMessageReceived deliveryEvent)
        {
            var subscription = new CMqSubscription(queueName, deliveryEvent);

            _subscriptions.Write(s =>
            {
                if (s.ContainsKey(queueName))
                {
                    throw new Exception($"Client is already subscribed to queue [{queueName}].");
                }

                s[queueName] = subscription;
            });

            var result = await _rmClient.QueryAsync(new CMqSubscribeToQueueQuery(queueName));
            result.ThrowIfFailed();

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

            _subscriptions.Write(s =>
            {
                if (s.ContainsKey(queueName))
                {
                    throw new Exception($"Client is already subscribed to queue [{queueName}].");
                }

                s[queueName] = subscription;
            });

            var result = _rmClient.Query(new CMqSubscribeToQueueQuery(queueName)).Result;
            result.ThrowIfFailed();

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

            _subscriptions.Write(s =>
            {
                if (s.ContainsKey(queueName))
                {
                    throw new Exception($"Client is already subscribed to queue [{queueName}].");
                }

                s[queueName] = subscription;
            });

            var result = await _rmClient.QueryAsync(new CMqSubscribeToQueueQuery(queueName));
            result.ThrowIfFailed();

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

            _subscriptions.Write(s =>
            {
                if (s.ContainsKey(queueName))
                {
                    throw new Exception($"Client is already subscribed to queue [{queueName}].");
                }

                s[queueName] = subscription;
            });

            var result = _rmClient.Query(new CMqSubscribeToQueueQuery(queueName)).Result;
            result.ThrowIfFailed();

            StartBufferedDeliveryTask();

            return subscription;
        }

        private void StartBufferedDeliveryTask()
        {
            try
            {
                if (_bufferedDeliveryTask == null)
                {
                    lock (this)
                    {
                        _bufferedDeliveryTask ??= Task.Run(BufferedDeliveryTaskProc);
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
                while (!_explicitDisconnect)
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
                        if (mb.Count == 0)
                        {
                            return;
                        }

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
        /// Flush the entire message buffer to the subscribed event.
        /// </summary>
        /// <param name="ensureEmpty">Whether or not to continue until the message buffer is empty.
        /// We do this because we *typically* allow failure-to-lock to defer the event.</param>
        private void FlushBufferedMessages(bool ensureEmpty)
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
                                foreach (var messageBuffer in mb)
                                {
                                    if (messageBuffer.Value.Count == 0)
                                    {
                                        subscription.LastBufferFlushed = DateTime.UtcNow;
                                    }
                                    else if (messageBuffer.Value.Count >= subscription.BatchSize
                                        || (subscription.AutoFlushInterval != TimeSpan.Zero
                                        && (DateTime.UtcNow - subscription.LastBufferFlushed) >= subscription.AutoFlushInterval))
                                    {
                                        if (messageBuffer.Key == subscription.Id)
                                        {
                                            var bufferedValueClone = messageBuffer.Value.ToList();
                                            Task.Run(() => subscription.BatchDeliveryEvent?.Invoke(this, bufferedValueClone));
                                            messageBuffer.Value.Clear();
                                        }

                                        subscription.LastBufferFlushed = DateTime.UtcNow;
                                    }
                                }
                            }
                        }) && success;
                    }) && success;
                } while (!success && ensureEmpty);
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
            var existingSubscription = _subscriptions.Write(s =>
            {
                s.Remove(queueName, out var existingSubscription);
                return existingSubscription;
            }) ?? throw new Exception($"Client is not subscribed to queue [{queueName}].");

            if (existingSubscription != null)
            {
                var result = await _rmClient.QueryAsync(new CMqUnsubscribeFromQueueQuery(queueName));
                result.ThrowIfFailed();
            }
        }

        /// <summary>
        /// Instructs the server to stop notifying the client of messages sent to the given queue.
        /// </summary>
        public void Unsubscribe(string queueName)
        {
            var existingSubscription = _subscriptions.Write(s =>
            {
                s.Remove(queueName, out var existingSubscription);
                return existingSubscription;
            }) ?? throw new Exception($"Client is not subscribed to queue [{queueName}].");

            if (existingSubscription != null)
            {
                var result = _rmClient.Query(new CMqUnsubscribeFromQueueQuery(queueName)).Result;
                result.ThrowIfFailed();
            }
        }

        /// <summary>
        /// Dispatches a message to the queue server to be enqueued in the given queue.
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

            var result = await _rmClient.QueryAsync(new CMqEnqueueMessageToQueue(
                queueName, options?.DeferDeliveryDuration, objectType, messageJson), options?.ServerDeliveryTimeout);

            result.ThrowIfFailed();
        }

        /// <summary>
        /// Dispatches a pre-serialized message to the queue server to be enqueued in the given queue.
        /// </summary>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="assemblyQualifiedName">Fully assembly qualified type of the message type for deserialization.</param>
        /// <param name="messageJson">Json for payload message of type inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public async Task EnqueueAsync(string queueName, string assemblyQualifiedName, string messageJson, CMqEnqueueOptions? options = null)
        {
            var result = await _rmClient.QueryAsync(new CMqEnqueueMessageToQueue(
                queueName, options?.DeferDeliveryDuration, assemblyQualifiedName, messageJson), options?.ServerDeliveryTimeout);

            result.ThrowIfFailed();
        }

        /// <summary>
        /// Dispatches a message to the queue server to be enqueued in the given queue.
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

            var result = _rmClient.Query(new CMqEnqueueMessageToQueue(
                queueName, options?.DeferDeliveryDuration, objectType, messageJson), options?.ServerDeliveryTimeout).Result;

            result.ThrowIfFailed();
        }

        /// <summary>
        /// Dispatches a pre-serialized message to the queue server to be enqueued in the given queue.
        /// </summary>
        /// <param name="queueName">Name of the queue in which to place the message into.</param>
        /// <param name="assemblyQualifiedName">Fully assembly qualified type of the message type for deserialization.</param>
        /// <param name="messageJson">Json for payload message of type inheriting from ICMqMessage.</param>
        /// <param name="options">Options for message enqueuing.</param>
        public void Enqueue(string queueName, string assemblyQualifiedName, string messageJson, CMqEnqueueOptions? options = null)
        {
            var result = _rmClient.Query(new CMqEnqueueMessageToQueue(
                queueName, options?.DeferDeliveryDuration, assemblyQualifiedName, messageJson), options?.ServerDeliveryTimeout).Result;

            result.ThrowIfFailed();
        }

        /// <summary>
        /// Disconnects the client from the queue server.
        /// This does not need to be called if Disconnect() is called.
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }
    }
}
