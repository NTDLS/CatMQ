using Newtonsoft.Json;
using NTDLS.CatMQ.Client.Client.QueryHandlers;
using NTDLS.CatMQ.Shared;
using NTDLS.CatMQ.Shared.Payload.ClientToServer;
using NTDLS.ReliableMessaging;
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
        private readonly Dictionary<string, SubscriptionReference> _queueSubscriptionReferences = new(StringComparer.OrdinalIgnoreCase);

        class SubscriptionReference
        {
            public int Count  { get; set; }
        }


        /// <summary>
        /// Whether or not the client has the OnReceivedUnboxed event hooked.
        /// </summary>
        internal bool ProcessUnboxedMessages => OnReceivedUnboxed != null;

        /// <summary>
        /// Whether or not the client has the OnReceivedBoxed event hooked.
        /// </summary>
        internal bool ProcessBoxedMessages => OnReceivedBoxed != null;

        private string? _lastReconnectHost;
        private int _lastReconnectPort;
        private IPAddress? _lastReconnectIpAddress;

        /// <summary>
        /// Returns true if the client is connected.
        /// </summary>
        public bool IsConnected => _rmClient.IsConnected;

        /// <summary>
        /// Delegate used for server-to-client deserialized delivery notifications.
        /// These messages are automatically deserialized, but this requires that
        /// the client assembly contain the references to any appropriate classes that are to be deserialized.
        /// </summary>
        /// <returns>Return true to mark the message as consumed by the client.</returns>
        public delegate CMqConsumptionResult OnReceivedUnboxedEvent(CMqClient client, string queueName, ICMqMessage message);

        /// <summary>
        /// Event used for server-to-client deserialized delivery notifications.
        /// These messages are automatically deserialized, but this requires that
        /// the client assembly contain the references to any appropriate classes that are to be deserialized.
        /// </summary>
        /// <returns>Return true to mark the message as consumed by the client.</returns>
        public event OnReceivedUnboxedEvent? OnReceivedUnboxed;

        /// <summary>
        /// Delegate used for server-to-client delivery notifications containing raw JSON.
        /// </summary>
        /// <returns>Return true to mark the message as consumed by the client.</returns>
        public delegate CMqConsumptionResult OnReceivedBoxedEvent(CMqClient client, string queueName, string objectType, string message);

        /// <summary>
        /// Event used for server-to-client delivery notifications.
        /// </summary>
        /// <returns>Return true to mark the message as consumed by the client.</returns>
        public event OnReceivedBoxedEvent? OnReceivedBoxed;

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

        internal bool InvokeOnReceivedUnboxed(CMqClient client, string queueName, ICMqMessage message)
        {
            bool wasConsumed = false;
            if (OnReceivedUnboxed != null)
            {
                foreach (var handler in OnReceivedUnboxed.GetInvocationList().Cast<OnReceivedUnboxedEvent>())
                {
                    var consume = handler(client, queueName, message);
                    if (consume == CMqConsumptionResult.Consumed)
                    {
                        wasConsumed = true;
                    }
                }
            }
            return wasConsumed;
        }

        internal bool InvokeOnReceivedBoxed(CMqClient client, string queueName, string objectType, string message)
        {
            bool wasConsumed = false;
            if (OnReceivedBoxed != null)
            {
                foreach (var handler in OnReceivedBoxed.GetInvocationList().Cast<OnReceivedBoxedEvent>())
                {
                    var consume = handler(client, queueName, objectType, message);
                    if (consume == CMqConsumptionResult.Consumed)
                    {
                        wasConsumed = true;
                    }
                }
            }
            return wasConsumed;
        }

        internal void InvokeOnException(CMqClient client, string? queueName, Exception ex)
            => OnException?.Invoke(client, queueName, ex);

        /// <summary>
        /// Connects the client to a queue server.
        /// </summary>
        public void Connect(string hostName, int port)
        {
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
            _lastReconnectHost = null;
            _lastReconnectIpAddress = ipAddress;
            _lastReconnectPort = port;

            _explicitDisconnect = false;

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
        public void Subscribe(string queueName)
        {
            lock (_queueSubscriptionReferences)
            {
                if (_queueSubscriptionReferences.TryGetValue(queueName, out var reference))
                {
                    reference.Count++;
                }
                else
                {
                    _queueSubscriptionReferences.Add(queueName, new SubscriptionReference() { Count = 1 });
                }
            }

            var result = _rmClient.Query(new CMqSubscribeToQueueQuery(queueName)).Result;
            if (result.IsSuccess == false)
            {
                throw new Exception(result.ErrorMessage);
            }
        }

        /// <summary>
        /// Instructs the server to stop notifying the client of messages sent to the given queue.
        /// </summary>
        public void Unsubscribe(string queueName)
        {
            bool lastQueueSubscription = false;
            lock (_queueSubscriptionReferences)
            {
                if (_queueSubscriptionReferences.TryGetValue(queueName, out var reference))
                {
                    reference.Count--;
                    lastQueueSubscription = reference.Count == 0;
                }
            }

            if (lastQueueSubscription)
            {
                var result = _rmClient.Query(new CMqUnsubscribeFromQueueQuery(queueName)).Result;
                if (result.IsSuccess == false)
                {
                    throw new Exception(result.ErrorMessage);
                }
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
