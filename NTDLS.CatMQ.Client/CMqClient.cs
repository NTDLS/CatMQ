using Newtonsoft.Json;
using NTDLS.CatMQ.Client.Client.QueryHandlers;
using NTDLS.CatMQ.Shared;
using NTDLS.CatMQ.Shared.Payload.ClientToServer;
using NTDLS.ReliableMessaging;
using NTDLS.Semaphore;
using System.Net;

namespace NTDLS.CatMQ.Client
{
    /// <summary>
    /// Connects to a MessageServer then sends/received and processes notifications/queries.
    /// </summary>
    public class CMqClient
    {
        private static readonly JsonSerializerSettings _typeNameHandlingAll = new()
        {
            TypeNameHandling = TypeNameHandling.All
        };

        private readonly RmClient _rmClient;
        private bool _explicitDisconnect = false;
        private readonly CMqClientConfiguration _configuration;
        private readonly OptimisticCriticalResource<Dictionary<string, List<CMqSubscription>>> _subscriptions;
        private readonly OptimisticCriticalResource<Dictionary<Guid, List<CMqReceivedMessage>>> _messageBuffer = new();
        private Thread? _bufferThread;

        class SubscriptionReference
        {
            public int Count { get; set; }
        }

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
            _subscriptions = new(() => new Dictionary<string, List<CMqSubscription>>(StringComparer.OrdinalIgnoreCase));

            var rmConfiguration = new RmConfiguration()
            {
                AsynchronousQueryWaiting = configuration.AsynchronousAcknowledgment,
                InitialReceiveBufferSize = configuration.InitialReceiveBufferSize,
                MaxReceiveBufferSize = configuration.MaxReceiveBufferSize,
                QueryTimeout = configuration.QueryTimeout,
                ReceiveBufferGrowthRate = configuration.ReceiveBufferGrowthRate
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
            _subscriptions = new(() => new Dictionary<string, List<CMqSubscription>>(StringComparer.OrdinalIgnoreCase));
            _rmClient = new RmClient();

            _rmClient.OnConnected += RmClient_OnConnected;
            _rmClient.OnDisconnected += RmClient_OnDisconnected;

            _rmClient.AddHandler(new InternalClientQueryHandlers(this));
        }

        private void RmClient_OnConnected(RmContext context)
        {
            _explicitDisconnect = false;
            OnConnected?.Invoke(this);
        }

        private void RmClient_OnDisconnected(RmContext context)
        {
            OnDisconnected?.Invoke(this);

            if (_explicitDisconnect == false && _configuration.AutoReconnect)
            {
                new Thread((o) =>
                {
                    while (!_explicitDisconnect && !_rmClient.IsConnected)
                    {
                        try
                        {
                            if (_lastReconnectHost != null)
                            {
                                _rmClient.Connect(_lastReconnectHost, _lastReconnectPort);
                            }
                            else if (_lastReconnectIpAddress != null)
                            {
                                _rmClient.Connect(_lastReconnectIpAddress, _lastReconnectPort);
                            }
                            else
                            {
                                break; //What else can we do.
                            }
                        }
                        catch (Exception ex)
                        {
                            OnException?.Invoke(this, null, ex.GetBaseException());
                        }

                        Thread.Sleep(1000);
                    }
                }).Start();
            }
        }

        internal bool InvokeOnReceived(CMqClient client, CMqReceivedMessage message)
        {
            List<CMqSubscription>? filteredSubscriptions = null;

            _subscriptions.Read(s =>
            {
                if (s.TryGetValue(message.QueueName, out var subscriptions))
                {
                    filteredSubscriptions = subscriptions.ToList();
                }
            });

            bool wasConsumed = false;

            if (filteredSubscriptions != null)
            {
                foreach (var subscription in filteredSubscriptions)
                {
                    if (subscription.BufferSize != null && subscription.BufferedFunction != null)
                    {
                        _messageBuffer.Write(mb =>
                        {
                            if (mb.TryGetValue(subscription.Id, out var buffer))
                            {
                                buffer.Add(message);
                            }
                            else
                            {
                                mb.Add(subscription.Id, new List<CMqReceivedMessage> { message });
                            }
                        });

                        wasConsumed = true;
                    }
                    else if (subscription.MessageFunction != null)
                    {
                        bool consumed = subscription.MessageFunction.Invoke(client, message);
                        if (consumed)
                        {
                            wasConsumed = true;
                        }
                    }
                }
            }

            return wasConsumed;
        }

        internal void InvokeOnException(CMqClient client, string? queueName, Exception ex)
            => OnException?.Invoke(client, queueName, ex);

        private void BufferThreadProc(object? p)
        {
            while (!_explicitDisconnect)
            {
                FlushBufferedMessages();
                Thread.Sleep(1);
            }

            FlushBufferedMessages();
        }

        private void FlushBufferedMessages()
        {
            _messageBuffer.TryWrite(mb =>
            {
                if (mb.Count == 0)
                {
                    return;
                }

                _subscriptions.TryRead(s =>
                {
                    foreach (var subscriptions in s.Values)
                    {
                        foreach (var subscription in subscriptions)
                        {
                            foreach (var messageBuffer in mb)
                            {
                                if (messageBuffer.Value.Count >= subscription.BufferSize
                                    || (subscription.AutoFlushInterval != TimeSpan.Zero
                                        && (DateTime.UtcNow - subscription.LastBufferFlushed) >= subscription.AutoFlushInterval))
                                {
                                    if (messageBuffer.Key == subscription.Id)
                                    {
                                        var bufferedValueClone = messageBuffer.Value.ToList();
                                        Task.Run(() =>
                                            {
                                                subscription.BufferedFunction?.Invoke(this, bufferedValueClone);
                                            });
                                        messageBuffer.Value.Clear();
                                    }

                                    subscription.LastBufferFlushed = DateTime.UtcNow;
                                }
                            }
                        }
                    }
                });
            });
        }

        /// <summary>
        /// Connects the client to a queue server.
        /// </summary>
        public void Connect(string hostName, int port)
        {
            _lastReconnectHost = hostName;
            _lastReconnectIpAddress = null;
            _lastReconnectPort = port;

            _explicitDisconnect = false;

            _bufferThread = new Thread(BufferThreadProc);
            _bufferThread.Start();

            _rmClient.Connect(hostName, port);
        }

        /// <summary>
        /// Connects the client to a queue server.
        /// </summary>
        public void Connect(IPAddress ipAddress, int port)
        {
            _lastReconnectHost = null;
            _lastReconnectIpAddress = ipAddress;
            _lastReconnectPort = port;

            _explicitDisconnect = false;

            _bufferThread = new Thread(BufferThreadProc);
            _bufferThread.Start();

            _rmClient.Connect(ipAddress, port);
        }

        /// <summary>
        /// Connects the client to a queue server.
        /// </summary>
        public void ConnectAsync(string hostName, int port)
        {
            new Thread(() =>
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
            }).Start();
        }

        /// <summary>
        /// Connects the client to a queue server.
        /// </summary>
        public void ConnectAsync(IPAddress ipAddress, int port)
        {
            new Thread(() =>
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
            }).Start();
        }

        /// <summary>
        /// Disconnects the client from the queue server.
        /// </summary>
        public void Disconnect(bool wait = false)
        {
            _explicitDisconnect = true;
            _rmClient.Disconnect(wait);
            _bufferThread?.Join();
        }

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public void CreateQueue(string queueName)
        {
            var result = _rmClient.Query(new CMqCreateQueueQuery(new CMqQueueConfiguration(queueName))).Result;
            if (result.IsSuccess == false)
            {
                throw new Exception(result.ErrorMessage);
            }
        }

        /// <summary>
        /// Instructs the server to create a queue with the given name.
        /// </summary>
        public void CreateQueue(CMqQueueConfiguration queueConfiguration)
        {
            var result = _rmClient.Query(new CMqCreateQueueQuery(queueConfiguration)).Result;
            if (result.IsSuccess == false)
            {
                throw new Exception(result.ErrorMessage);
            }
        }

        /// <summary>
        /// Instructs the server to delete the queue with the given name.
        /// </summary>
        public void DeleteQueue(string queueName)
        {
            var result = _rmClient.Query(new CMqDeleteQueueQuery(queueName)).Result;
            if (result.IsSuccess == false)
            {
                throw new Exception(result.ErrorMessage);
            }
        }

        /// <summary>
        /// Instructs the server to remove all messages from the queue with the given name.
        /// </summary>
        public void PurgeQueue(string queueName)
        {
            var result = _rmClient.Query(new CMqPurgeQueueQuery(queueName)).Result;
            if (result.IsSuccess == false)
            {
                throw new Exception(result.ErrorMessage);
            }
        }

        /// <summary>
        /// Instructs the server to notify the client of messages sent to the given queue.
        /// </summary>
        public CMqSubscription Subscribe(string queueName, OnMessageReceived messageFunction)
        {
            bool wasFirstSubscriptionToThisQueue = false;

            var newSubscription = new CMqSubscription(queueName, messageFunction);

            _subscriptions.Write(s =>
            {
                if (s.TryGetValue(queueName, out var subscriptions))
                {
                    wasFirstSubscriptionToThisQueue = subscriptions.Count == 0;
                    subscriptions.Add(newSubscription);
                }
                else
                {
                    s.Add(queueName, new List<CMqSubscription>() { newSubscription });
                    wasFirstSubscriptionToThisQueue = true;
                }
            });

            if (wasFirstSubscriptionToThisQueue)
            {
                var result = _rmClient.Query(new CMqSubscribeToQueueQuery(queueName)).Result;
                if (result.IsSuccess == false)
                {
                    throw new Exception(result.ErrorMessage);
                }
            }

            return newSubscription;
        }

        /// <summary>
        /// Instructs the server to notify the client of messages sent to the given queue.
        /// </summary>
        public CMqSubscription SubscribeBuffered(string queueName, int bufferSize, TimeSpan autoFlushInterval, OnBatchReceived batchFunction)
        {
            bool wasFirstSubscriptionToThisQueue = false;

            var newSubscription = new CMqSubscription(queueName, bufferSize, autoFlushInterval, batchFunction);

            _subscriptions.Write(s =>
            {
                if (s.TryGetValue(queueName, out var subscriptions))
                {
                    wasFirstSubscriptionToThisQueue = subscriptions.Count == 0;
                    subscriptions.Add(newSubscription);
                }
                else
                {
                    s.Add(queueName, new List<CMqSubscription>() { newSubscription });
                    wasFirstSubscriptionToThisQueue = true;
                }
            });

            if (wasFirstSubscriptionToThisQueue)
            {
                var result = _rmClient.Query(new CMqSubscribeToQueueQuery(queueName)).Result;
                if (result.IsSuccess == false)
                {
                    throw new Exception(result.ErrorMessage);
                }
            }

            return newSubscription;
        }

        /// <summary>
        /// Removes the subscription for the specified subscription descriptor.
        /// </summary>
        public void Unsubscribe(CMqSubscription subscription)
        {
            bool wasLastSubscriptionToThisQueue = false;

            _subscriptions.Write(s =>
            {
                if (s.TryGetValue(subscription.QueueName, out var subscriptions))
                {
                    subscriptions.RemoveAll(o => o.Id == subscription.Id);
                    wasLastSubscriptionToThisQueue = subscriptions.Count == 0;
                }
                else
                {
                    wasLastSubscriptionToThisQueue = true;
                }
            });

            if (wasLastSubscriptionToThisQueue)
            {
                //We only actually unsubscribe from the server queue when we have removed all subscribed client events.
                var result = _rmClient.Query(new CMqUnsubscribeFromQueueQuery(subscription.QueueName)).Result;
                if (result.IsSuccess == false)
                {
                    throw new Exception(result.ErrorMessage);
                }
            }
        }

        /// <summary>
        /// Instructs the server to stop notifying the client of messages sent to the given queue.
        /// </summary>
        public void UnsubscribeAll(string queueName)
        {
            var result = _rmClient.Query(new CMqUnsubscribeFromQueueQuery(queueName)).Result;
            if (result.IsSuccess == false)
            {
                throw new Exception(result.ErrorMessage);
            }
        }

        /// <summary>
        /// Dispatches a message to the queue server to be enqueued in the given queue.
        /// </summary>
        public void Enqueue<T>(string queueName, T message)
            where T : ICMqMessage
        {
            var messageJson = JsonConvert.SerializeObject(message, _typeNameHandlingAll);
            var assemblyQualifiedName = message.GetType()?.AssemblyQualifiedName ?? string.Empty;
            var parts = assemblyQualifiedName.Split(','); //We only want the first two parts, not the version and such.
            var objectType = parts.Length > 1 ? $"{parts[0]},{parts[1].Trim()}" : assemblyQualifiedName;

            var result = _rmClient.Query(new CMqEnqueueMessageToQueue(queueName, objectType, messageJson)).Result;
            if (result.IsSuccess == false)
            {
                throw new Exception(result.ErrorMessage);
            }
        }
    }
}
