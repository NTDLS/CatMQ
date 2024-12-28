using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Server.Server;
using NTDLS.CatMQ.Server.Server.QueryHandlers;
using NTDLS.CatMQ.Shared;
using NTDLS.CatMQ.Shared.Payloads.Queries.ServerToClient;
using NTDLS.ReliableMessaging;
using NTDLS.Semaphore;
using RocksDbSharp;
using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;

namespace NTDLS.CatMQ.Server
{
    /// <summary>
    /// Listens for connections from MessageClients and processes the incoming notifications/queries.
    /// </summary>
    public class CMqServer
    {
        private const int _deadlockAvoidanceWaitMs = 10;
        private readonly RmServer _rmServer;
        private readonly PessimisticCriticalResource<CaseInsensitiveMessageQueueDictionary> _messageQueues = new();
        private readonly CMqServerConfiguration _configuration;
        private RocksDb? _persistenceDatabase;
        private bool _keepRunning = false;

        /// <summary>
        /// Delegate used to notify of queue server exceptions.
        /// </summary>
        public delegate void OnLogEvent(CMqServer server, ErrorLevel errorLevel, string message, Exception? ex = null);

        /// <summary>
        /// Event used to notify of queue server exceptions.
        /// </summary>
        public event OnLogEvent? OnLog;

        /// <summary>
        /// Creates a new instance of the queue service.
        /// </summary>
        public CMqServer(CMqServerConfiguration configuration)
        {
            _configuration = configuration;

            var rmConfiguration = new RmConfiguration()
            {
                AsynchronousQueryWaiting = configuration.AsynchronousQueryWaiting,
                InitialReceiveBufferSize = configuration.InitialReceiveBufferSize,
                MaxReceiveBufferSize = configuration.MaxReceiveBufferSize,
                QueryTimeout = configuration.QueryTimeout,
                ReceiveBufferGrowthRate = configuration.ReceiveBufferGrowthRate,
            };

            _rmServer = new RmServer(rmConfiguration);
            _rmServer.AddHandler(new InternalServerQueryHandlers(this));
            _rmServer.OnDisconnected += RmServer_OnDisconnected;
        }

        /// <summary>
        /// Creates a new instance of the queue service.
        /// </summary>
        public CMqServer()
        {
            _configuration = new CMqServerConfiguration();
            _rmServer = new RmServer();
            _rmServer.AddHandler(new InternalServerQueryHandlers(this));
            _rmServer.OnDisconnected += RmServer_OnDisconnected;
        }

        #region Management.

        /// <summary>
        /// Saves persistent message queues and their statistics to disk.
        /// </summary>
        public void CheckpointPersistentMessageQueues()
        {
            _messageQueues.Use(mqd => CheckpointPersistentMessageQueues(mqd));
        }

        private void CheckpointPersistentMessageQueues(CaseInsensitiveMessageQueueDictionary mqd)
        {
            if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
            {
                OnLog?.Invoke(this, ErrorLevel.Verbose, "Checkpoint persistent queues.");

                var persistedQueues = mqd.Where(q => q.Value.QueueConfiguration.Persistence == PMqPersistence.Persistent).Select(q => q.Value).ToList();

                //Serialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                var persistedQueuesJson = JsonSerializer.Serialize(persistedQueues);
                File.WriteAllText(Path.Join(_configuration.PersistencePath, "queues.json"), persistedQueuesJson);
            }
        }

        /// <summary>
        /// Returns a read-only copy of the running configuration.
        /// </summary>
        /// <returns></returns>
        public CMqServerInformation GetConfiguration()
        {
            return new CMqServerInformation
            {
                AsynchronousQueryWaiting = _configuration.AsynchronousQueryWaiting,
                QueryTimeout = _configuration.QueryTimeout,
                InitialReceiveBufferSize = _configuration.InitialReceiveBufferSize,
                MaxReceiveBufferSize = _configuration.MaxReceiveBufferSize,
                ReceiveBufferGrowthRate = _configuration.ReceiveBufferGrowthRate,
                ListenPort = _rmServer.ListenPort,
                PersistencePath = _configuration.PersistencePath
            };
        }

        /// <summary>
        /// Returns a read-only copy of the queues.
        /// </summary>
        public ReadOnlyCollection<CMqQueueInformation> GetQueues()
        {
            while (true)
            {
                bool success = true;
                List<CMqQueueInformation>? result = new();

                success = _messageQueues.TryUse(mqd =>
                {
                    foreach (var mqKVP in mqd)
                    {
                        success = mqKVP.Value.EnqueuedMessages.TryUse(m =>
                        {
                            success = mqKVP.Value.Subscribers.TryUse(sKVP =>
                            {
                                result.Add(new CMqQueueInformation
                                {
                                    BatchDeliveryInterval = mqKVP.Value.QueueConfiguration.BatchDeliveryInterval,
                                    ConsumptionScheme = mqKVP.Value.QueueConfiguration.ConsumptionScheme,
                                    CurrentEnqueuedMessageCount = m.Count,
                                    DeliveryScheme = mqKVP.Value.QueueConfiguration.DeliveryScheme,
                                    DeliveryThrottle = mqKVP.Value.QueueConfiguration.DeliveryThrottle,
                                    MaxDeliveryAttempts = mqKVP.Value.QueueConfiguration.MaxDeliveryAttempts,
                                    MaxMessageAge = mqKVP.Value.QueueConfiguration.MaxMessageAge,
                                    Persistence = mqKVP.Value.QueueConfiguration.Persistence,
                                    QueueName = mqKVP.Value.QueueConfiguration.QueueName,
                                    SubscriberCount = sKVP.Count,
                                    TotalDeliveredMessages = mqKVP.Value.TotalDeliveredMessages,
                                    TotalDeliveryFailures = mqKVP.Value.TotalDeliveryFailures,
                                    TotalEnqueuedMessages = mqKVP.Value.TotalEnqueuedMessages,
                                    TotalExpiredMessages = mqKVP.Value.TotalExpiredMessages
                                });
                            }) && success;
                        }) && success;

                        if (!success)
                        {
                            //Failed to lock, break the inner loop and try again.
                            result = null;
                            break;
                        }
                    }
                }) && success;

                if (success && result != null)
                {
                    return new ReadOnlyCollection<CMqQueueInformation>(result);
                }

                Thread.Sleep(_deadlockAvoidanceWaitMs); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy of the queue subscribers.
        /// </summary>
        public ReadOnlyCollection<CMqSubscriberInformation> GetSubscribers(string queueName)
        {
            while (true)
            {
                bool success = true;
                var result = new List<CMqSubscriberInformation>();

                success = _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueName, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryUse(sKVP =>
                        {
                            foreach (var subscriber in sKVP)
                            {
                                result.Add(subscriber.Value);
                            }
                        }) && success;
                    }
                    else
                    {
                        throw new Exception($"Queue not found: [{queueName}].");
                    }
                }) && success;

                if (success)
                {
                    return new ReadOnlyCollection<CMqSubscriberInformation>(result);
                }

                Thread.Sleep(_deadlockAvoidanceWaitMs); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy messages in the queue.
        /// </summary>
        public ReadOnlyCollection<CMqEnqueuedMessageInformation> GetQueueMessages(string queueName, int offset, int take)
        {
            while (true)
            {
                bool success = true;
                List<CMqEnqueuedMessageInformation>? result = new();

                success = _messageQueues.TryUse(mqd =>
                {
                    var filteredQueues = mqd.Where(o => o.Value.QueueConfiguration.QueueName.Equals(queueName, StringComparison.OrdinalIgnoreCase));
                    foreach (var qKVP in filteredQueues)
                    {
                        success = qKVP.Value.EnqueuedMessages.TryUse(m =>
                        {
                            success = qKVP.Value.Subscribers.TryUse(sKVP =>
                            {
                                foreach (var message in m.Skip(offset).Take(take))
                                {
                                    result.Add(new CMqEnqueuedMessageInformation
                                    {
                                        Timestamp = message.Timestamp,
                                        SubscriberCount = sKVP.Count,
                                        SubscriberMessageDeliveries = message.SubscriberMessageDeliveries.Keys.ToHashSet(),
                                        SatisfiedSubscribersConnectionIDs = message.SatisfiedSubscribersConnectionIDs,
                                        ObjectType = message.ObjectType,
                                        MessageJson = message.MessageJson,
                                        MessageId = message.MessageId
                                    });
                                }
                            }) && success;
                        }) && success;

                        if (!success)
                        {
                            //Failed to lock, break the inner loop and try again.
                            result = null;
                            break;
                        }
                    }
                }) && success;

                if (success && result != null)
                {
                    return new ReadOnlyCollection<CMqEnqueuedMessageInformation>(result);
                }

                Thread.Sleep(_deadlockAvoidanceWaitMs); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy messages in the queue.
        /// </summary>
        public CMqEnqueuedMessageInformation GetQueueMessage(string queueName, Guid messageId)
        {
            while (true)
            {
                bool success = true;
                CMqEnqueuedMessageInformation? result = null;

                success = _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueName, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryUse(m =>
                        {
                            var message = m.Where(o => o.MessageId == messageId).FirstOrDefault();
                            if (message != null)
                            {
                                result = new CMqEnqueuedMessageInformation
                                {
                                    Timestamp = message.Timestamp,
                                    SubscriberMessageDeliveries = message.SubscriberMessageDeliveries.Keys.ToHashSet(),
                                    SatisfiedSubscribersConnectionIDs = message.SatisfiedSubscribersConnectionIDs,
                                    ObjectType = message.ObjectType,
                                    MessageJson = message.MessageJson,
                                    MessageId = message.MessageId
                                };
                            }
                            else
                            {
                                throw new Exception($"Message not found: [{messageId}].");
                            }
                        }) && success;
                    }
                    else
                    {
                        throw new Exception($"Queue not found: [{queueName}].");
                    }
                }) && success;

                if (success && result != null)
                {
                    return result;
                }

                Thread.Sleep(1); //Failed to lock, sleep then try again.
            }
        }

        #endregion

        internal void InvokeOnLog(CMqServer server, Exception ex)
            => OnLog?.Invoke(server, ErrorLevel.Error, ex.Message, ex);

        internal void InvokeOnLog(CMqServer server, ErrorLevel errorLevel, string message)
            => OnLog?.Invoke(server, errorLevel, message);

        private void RmServer_OnDisconnected(RmContext context)
        {
            while (true)
            {
                bool success = true;

                //When a client disconnects, remove their subscriptions.
                success = _messageQueues.TryUse(mqd =>
                {
                    foreach (var mqKVP in mqd)
                    {
                        success = mqKVP.Value.Subscribers.TryUse(s =>
                        {
                            s.Remove(context.ConnectionId);
                        }) && success;
                        if (!success)
                        {
                            break;
                        }
                    }
                }) && success;

                if (success)
                {
                    return;
                }
                Thread.Sleep(_deadlockAvoidanceWaitMs);
            }
        }

        #region Start & Stop.

        /// <summary>
        /// Starts the message queue server.
        /// </summary>
        public void Start(int listenPort)
        {
            if (_keepRunning)
            {
                return;
            }

            _keepRunning = true;

            if (_configuration.PersistencePath != null)
            {
                #region Load and create persisted queues.

                List<MessageQueue>? persistedQueues = null;

                var persistedQueuesFile = Path.Join(_configuration.PersistencePath, "queues.json");
                if (File.Exists(persistedQueuesFile))
                {
                    OnLog?.Invoke(this, ErrorLevel.Information, "Loading persistent queues.");

                    var persistedQueuesJson = File.ReadAllText(persistedQueuesFile);
                    //Deserialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                    var loadedPersistedQueues = JsonSerializer.Deserialize<List<MessageQueue>>(persistedQueuesJson);

                    if (loadedPersistedQueues != null)
                    {
                        _messageQueues.Use(mqd =>
                        {
                            foreach (var persistedQueue in loadedPersistedQueues)
                            {
                                persistedQueue.SetServer(this);
                                string queueKey = persistedQueue.QueueConfiguration.QueueName.ToLower();
                                if (mqd.ContainsKey(queueKey) == false)
                                {
                                    mqd.Add(queueKey, persistedQueue);
                                    persistedQueue.Start();
                                }
                            }
                        });

                        persistedQueues = loadedPersistedQueues;
                    }
                }

                #endregion

                #region Load persisted messages.

                //The keys in RocksDB are not stored in the order they were added, so we
                // need to load all messages into the messages queues then sort them in place.

                RocksDb? persistenceDatabase = null;

                string databaseFile = Path.Join(_configuration.PersistencePath, "messages");

                OnLog?.Invoke(this, ErrorLevel.Information, "Instantiating persistent database.");

                var options = new DbOptions()
                    .SetCreateIfMissing(true);
                persistenceDatabase = RocksDb.Open(options, databaseFile);

                if (persistedQueues != null)
                {
                    OnLog?.Invoke(this, ErrorLevel.Information, "Loading persistent messages.");

                    using var iterator = persistenceDatabase.NewIterator();
                    _messageQueues.Use(mqd =>
                    {
                        for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                        {
                            //Deserialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                            var persistedMessage = JsonSerializer.Deserialize<EnqueuedMessage>(iterator.StringValue());
                            if (persistedMessage != null)
                            {
                                if (mqd.TryGetValue(persistedMessage.QueueName, out var messageQueue))
                                {
                                    messageQueue.EnqueuedMessages.Use(m =>
                                    {
                                        m.Add(persistedMessage);
                                    });
                                }
                            }
                        }

                        //Sort the message in the queues by their timestamps.
                        OnLog?.Invoke(this, ErrorLevel.Information, "Sorting persistent messages.");
                        var tasks = mqd.Values.Select(mq => Task.Run(() => mq.SortMessages()));
                        Task.WhenAll(tasks).Wait();
                    });
                }

                #endregion

                _persistenceDatabase = persistenceDatabase;
            }

            _rmServer.Start(listenPort);

            new Thread(() => HeartbeatThread()).Start();
        }

        private void HeartbeatThread()
        {
            var lastCheckpoint = DateTime.UtcNow;

            while (_keepRunning)
            {
                if (DateTime.UtcNow - lastCheckpoint > TimeSpan.FromSeconds(30))
                {
                    //While the RockDB WAL logs data, it’s a good idea to flush the MemTable to disk periodically for additional safety.
                    OnLog?.Invoke(this, ErrorLevel.Verbose, "Checkpoint persistent database.");
                    _persistenceDatabase?.Flush(new FlushOptions());

                    CheckpointPersistentMessageQueues();
                    lastCheckpoint = DateTime.UtcNow;
                }

                Thread.Sleep(100);
            }
        }


        /// <summary>
        /// Stops the message queue server.
        /// </summary>
        public void Stop()
        {
            OnLog?.Invoke(this, ErrorLevel.Information, "Stopping service.");

            _keepRunning = false;
            OnLog?.Invoke(this, ErrorLevel.Information, "Disposing database instance.");
            _persistenceDatabase?.Dispose();
            _persistenceDatabase = null;
            OnLog?.Invoke(this, ErrorLevel.Information, "Stopping reliable messaging.");
            _rmServer.Stop();

            _messageQueues.Use(mqd =>
            {
                //Stop all message queues.
                foreach (var mqKVP in mqd)
                {
                    OnLog?.Invoke(this, ErrorLevel.Information, $"Stopping queue [{mqKVP.Value.QueueConfiguration.QueueName}].");
                    mqKVP.Value.Stop();
                }

                if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
                {
                    CheckpointPersistentMessageQueues(mqd);
                }
            });
        }

        #endregion

        #region Message queue interactions.

        /// <summary>
        /// Deliver a message from a server queue to a subscribed client.
        /// </summary>
        internal bool DeliverMessage(Guid connectionId, string queueName, EnqueuedMessage enqueuedMessage)
        {
            var result = _rmServer.Query(connectionId, new CMqMessageDeliveryQuery(queueName, enqueuedMessage.ObjectType, enqueuedMessage.MessageJson)).Result;
            if (string.IsNullOrEmpty(result.ErrorMessage) == false)
            {
                throw new Exception(result.ErrorMessage);
            }
            return result.WasMessageConsumed;
        }

        /// <summary>
        /// Removes a message from the persistent store.
        /// </summary>
        internal void RemovePersistenceMessage(string queueName, Guid messageId)
        {
            if (_persistenceDatabase != null)
            {
                OnLog?.Invoke(this, ErrorLevel.Verbose, $"Removing persistent message from [{queueName}]: [{messageId}].");
                string queueKey = queueName.ToLowerInvariant();
                lock (_persistenceDatabase)
                {
                    _persistenceDatabase.Remove($"{queueKey}_{messageId}");
                }
            }
        }

        #endregion

        #region Client interactions.

        /// <summary>
        /// Creates a new empty queue if it does not already exist.
        /// </summary>
        internal void CreateQueue(CMqQueueConfiguration queueConfiguration)
        {
            OnLog?.Invoke(this, ErrorLevel.Verbose, $"Creating queue: [{queueConfiguration.QueueName}].");

            _messageQueues.Use(mqd =>
            {
                string queueKey = queueConfiguration.QueueName.ToLowerInvariant();
                if (mqd.ContainsKey(queueKey) == false)
                {
                    var messageQueue = new MessageQueue(this, queueConfiguration);
                    mqd.Add(queueKey, messageQueue);

                    if (queueConfiguration.Persistence == PMqPersistence.Persistent)
                    {
                        if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
                        {
                            CheckpointPersistentMessageQueues(mqd);
                        }
                        else
                        {
                            throw new Exception("The server persistence path is not configured.");
                        }
                    }

                    messageQueue.Start();
                }
            });
        }

        /// <summary>
        /// Deletes an existing queue.
        /// </summary>
        internal void DeleteQueue(string queueName)
        {
            OnLog?.Invoke(this, ErrorLevel.Verbose, $"Deleting queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryUse(m =>
                        {
                            messageQueue.Stop();
                            mqd.Remove(queueKey);

                            if (_persistenceDatabase != null)
                            {
                                foreach (var message in m)
                                {
                                    lock (_persistenceDatabase)
                                    {
                                        _persistenceDatabase.Remove($"{queueKey}_{message.MessageId}");
                                    }
                                }
                            }
                        }) && success;

                        if (success)
                        {
                            if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
                            {
                                CheckpointPersistentMessageQueues(mqd);
                            }
                        }
                    }
                }) && success;

                if (success)
                {
                    return;
                }
                Thread.Sleep(_deadlockAvoidanceWaitMs);
            }
        }

        /// <summary>
        /// Creates a subscription to a queue for a given connection id.
        /// </summary>
        internal void SubscribeToQueue(Guid connectionId, IPEndPoint? localEndpoint, IPEndPoint? remoteEndpoint, string queueName)
        {
            OnLog?.Invoke(this, ErrorLevel.Verbose, $"Subscribing connection [{connectionId}] to queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryUse(s =>
                        {
                            if (s.ContainsKey(connectionId) == false)
                            {
                                s.Add(connectionId, new CMqSubscriberInformation(connectionId)
                                {
                                    LocalAddress = localEndpoint?.Address?.ToString(),
                                    RemoteAddress = remoteEndpoint?.Address?.ToString(),
                                    LocalPort = localEndpoint?.Port,
                                    RemotePort = remoteEndpoint?.Port
                                });
                            }
                        }) && success;
                    }
                }) && success;

                if (success)
                {
                    return;
                }
                Thread.Sleep(_deadlockAvoidanceWaitMs);
            }
        }

        /// <summary>
        /// Removes a subscription from a queue for a given connection id.
        /// </summary>
        internal void UnsubscribeFromQueue(Guid connectionId, string queueName)
        {
            OnLog?.Invoke(this, ErrorLevel.Verbose, $"Unsubscribing connection [{connectionId}] from queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryUse(s =>
                        {
                            s.Remove(connectionId);
                        }) && success;
                    }
                }) && success;

                if (success)
                {
                    return;
                }
                Thread.Sleep(_deadlockAvoidanceWaitMs);
            }
        }

        /// <summary>
        /// Removes a subscription from a queue for a given connection id.
        /// </summary>
        internal void EnqueueMessage(Guid connectionId, string queueName, string objectType, string messageJson)
        {
            OnLog?.Invoke(this, ErrorLevel.Verbose, $"Enqueuing message from connection [{connectionId}] to queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryUse(m =>
                        {
                            messageQueue.TotalEnqueuedMessages++;
                            var message = new EnqueuedMessage(queueKey, objectType, messageJson);
                            if (_persistenceDatabase != null)
                            {
                                //Serialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                                var persistedJson = JsonSerializer.Serialize(message);
                                lock (_persistenceDatabase)
                                {
                                    _persistenceDatabase.Put($"{queueKey}_{message.MessageId}", persistedJson);
                                }
                            }

                            m.Add(message);
                            messageQueue.DeliveryThreadWaitEvent.Set();
                        }) && success;
                    }
                    else
                    {
                        throw new Exception($"Queue not found: [{queueName}].");
                    }
                }) && success;

                if (success)
                {
                    return;
                }
                Thread.Sleep(_deadlockAvoidanceWaitMs);
            }
        }

        /// <summary>
        /// Removes all messages from the given queue.
        /// </summary>
        internal void PurgeQueue(string queueName)
        {
            OnLog?.Invoke(this, ErrorLevel.Verbose, $"Purging queue: [{queueName}].");

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryUse(mqd =>
                {
                    string queueKey = queueName.ToLowerInvariant();
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryUse(m =>
                        {
                            if (_persistenceDatabase != null)
                            {
                                foreach (var message in m)
                                {
                                    lock (_persistenceDatabase)
                                    {
                                        _persistenceDatabase.Remove($"{queueKey}_{message.MessageId}");
                                    }
                                }
                            }
                            m.Clear();
                        }) && success;
                    }
                    else
                    {
                        throw new Exception($"Queue not found: [{queueName}].");
                    }
                }) && success;

                if (success)
                {
                    return;
                }
                Thread.Sleep(_deadlockAvoidanceWaitMs);
            }
        }

        #endregion
    }
}
