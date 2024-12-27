using Newtonsoft.Json;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.QueryHandlers;
using NTDLS.CatMQServer.Management;
using NTDLS.CatMQShared;
using NTDLS.CatMQShared.Payloads.Queries.ServerToClient;
using NTDLS.ReliableMessaging;
using NTDLS.Semaphore;
using RocksDbSharp;
using System.Collections.ObjectModel;
using System.Net;

namespace NTDLS.CatMQServer
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
        public delegate void OnExceptionEvent(CMqServer server, CMqQueueConfiguration? queue, Exception ex);

        /// <summary>
        /// Event used to notify of queue server exceptions.
        /// </summary>
        public event OnExceptionEvent? OnException;

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
                //TODO: checkpoint this from time to time.
                var persistedQueues = mqd.Where(q => q.Value.QueueConfiguration.Persistence == PMqPersistence.Persistent).Select(q => q.Value).ToList();
                var persistedQueuesJson = JsonConvert.SerializeObject(persistedQueues);
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
                bool success = false;
                List<CMqQueueInformation>? result = new();

                success = _messageQueues.TryUse(mqd =>
                {
                    foreach (var mqKVP in mqd)
                    {
                        var enqueuedMessagesSuccess = mqKVP.Value.EnqueuedMessages.TryUse(m =>
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
                                QueueName = mqKVP.Value.QueueConfiguration.QueueName,
                                TotalDeliveredMessages = mqKVP.Value.TotalDeliveredMessages,
                                TotalEnqueuedMessages = mqKVP.Value.TotalEnqueuedMessages,
                                TotalExpiredMessages = mqKVP.Value.TotalExpiredMessages
                            });
                        });

                        if (!enqueuedMessagesSuccess)
                        {
                            //Failed to lock, break the inner loop and try again.
                            result = null;
                            break;
                        }
                    }
                });

                if (success && result != null)
                {
                    return new ReadOnlyCollection<CMqQueueInformation>(result);
                }

                Thread.Sleep(1); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy of the queue subscribers.
        /// </summary>
        public ReadOnlyCollection<CMqSubscriberInformation> GetSubscribers(string queueName)
        {
            while (true)
            {
                bool success = false;
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
                        });
                    }
                    else
                    {
                        throw new Exception($"Queue not found: [{queueName}].");
                    }
                });

                if (success)
                {
                    return new ReadOnlyCollection<CMqSubscriberInformation>(result);
                }

                Thread.Sleep(1); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy messages in the queue.
        /// </summary>
        public ReadOnlyCollection<CMqEnqueuedMessageInformation> GetQueueMessages(string queueName, int offset, int take)
        {
            while (true)
            {
                bool success = false;
                List<CMqEnqueuedMessageInformation>? result = new();

                success = _messageQueues.TryUse(mqd =>
                {
                    var filteredQueues = mqd.Where(o => o.Value.QueueConfiguration.QueueName.Equals(queueName, StringComparison.OrdinalIgnoreCase));
                    foreach (var qKVP in filteredQueues)
                    {
                        var enqueuedMessagesSuccess = qKVP.Value.EnqueuedMessages.TryUse(m =>
                        {
                            foreach (var message in m.Skip(offset).Take(take))
                            {
                                result.Add(new CMqEnqueuedMessageInformation
                                {
                                    Timestamp = message.Timestamp,
                                    SubscriberMessageDeliveries = message.SubscriberMessageDeliveries.Keys.ToHashSet(),
                                    SatisfiedSubscribersConnectionIDs = message.SatisfiedSubscribersConnectionIDs,
                                    ObjectType = message.ObjectType,
                                    MessageJson = message.MessageJson,
                                    MessageId = message.MessageId
                                });
                            }
                        });

                        if (!enqueuedMessagesSuccess)
                        {
                            //Failed to lock, break the inner loop and try again.
                            result = null;
                            break;
                        }
                    }
                });

                if (success && result != null)
                {
                    return new ReadOnlyCollection<CMqEnqueuedMessageInformation>(result);
                }

                Thread.Sleep(1); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy messages in the queue.
        /// </summary>
        public CMqEnqueuedMessageInformation GetQueueMessage(string queueName, Guid messageId)
        {
            while (true)
            {
                bool success = false;
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
                        });
                    }
                    else
                    {
                        throw new Exception($"Queue not found: [{queueName}].");
                    }
                });

                if (success && result != null)
                {
                    return result;
                }

                Thread.Sleep(1); //Failed to lock, sleep then try again.
            }
        }

        #endregion

        internal void InvokeOnException(CMqServer server, CMqQueueConfiguration? queue, Exception ex)
            => OnException?.Invoke(server, queue, ex);

        private void RmServer_OnDisconnected(RmContext context)
        {
            while (true)
            {
                bool success = false;

                //When a client disconnects, remove their subscriptions.
                _messageQueues.TryUse(mqd =>
                {
                    foreach (var mqKVP in mqd)
                    {
                        mqKVP.Value.Subscribers.TryUse(s =>
                        {
                            success = true;

                            s.Remove(context.ConnectionId);
                        });
                    }
                });

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
                    var persistedQueuesJson = File.ReadAllText(persistedQueuesFile);
                    var loadedPersistedQueues = JsonConvert.DeserializeObject<List<MessageQueue>>(persistedQueuesJson);

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

                //The messages in RocksDB are not stored in order, so we need to load all messages into a dictonary by queue name
                //  so we can then sort them by their time stamps and add them back to the appropriate queues.

                var persistedQueueMessages = new Dictionary<string, List<EnqueuedMessage>>(StringComparer.OrdinalIgnoreCase);
                RocksDb? persistenceDatabase = null;

                string databaseFile = Path.Join(_configuration.PersistencePath, "messages");

                var options = new DbOptions()
                    .SetCreateIfMissing(true);
                persistenceDatabase = RocksDb.Open(options, databaseFile);

                if (persistedQueues != null)
                {
                    using (var iterator = persistenceDatabase.NewIterator())
                    {
                        for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                        {
                            var persistedMessage = JsonConvert.DeserializeObject<EnqueuedMessage>(iterator.StringValue());
                            if (persistedMessage != null)
                            {
                                if (persistedQueueMessages.TryGetValue(persistedMessage.QueueName, out var messageCollection))
                                {
                                    messageCollection.Add(persistedMessage);
                                }
                                else
                                {
                                    persistedQueueMessages.Add(persistedMessage.QueueName, new List<EnqueuedMessage>() { persistedMessage });
                                }
                            }
                        }
                    }
                }

                #endregion

                #region Enqueue persisted messages.

                //Loop though the persistent queues (names), sort the messages by their time-stamps and add them to the queues.

                if (persistedQueues != null)
                {
                    _messageQueues.Use(mqd =>
                    {
                        foreach (var persistedQueueMessage in persistedQueueMessages)
                        {
                            if (mqd.TryGetValue(persistedQueueMessage.Key, out var messageQueue))
                            {
                                messageQueue.EnqueuedMessages.Use(m =>
                                {
                                    var persistedMessages = persistedQueueMessage.Value.OrderBy(o => o.Timestamp);
                                    foreach (var persistedMessage in persistedMessages)
                                    {
                                        m.Add(persistedMessage);
                                    }
                                });
                            }
                        }
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
                    _persistenceDatabase?.Flush(new FlushOptions());
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
            _keepRunning = false;
            _persistenceDatabase?.Dispose();
            _persistenceDatabase = null;
            _rmServer.Stop();

            _messageQueues.Use(mqd =>
            {
                //Stop all message queues.
                foreach (var mqKVP in mqd)
                {
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
        internal void RemovePersistenceMessage(string queueName, Guid MessageId)
        {
            if (_persistenceDatabase != null)
            {
                string queueKey = queueName.ToLowerInvariant();
                lock (_persistenceDatabase)
                {
                    _persistenceDatabase.Remove($"{queueKey}_{MessageId}");
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
            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = false;
                _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        messageQueue.EnqueuedMessages.TryUse(m =>
                        {
                            success = true;

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
                        });

                        if (success)
                        {
                            if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
                            {
                                CheckpointPersistentMessageQueues(mqd);
                            }
                        }
                    }
                });

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
            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = false;
                _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        messageQueue.Subscribers.TryUse(s =>
                        {
                            success = true;

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
                        });
                    }
                });

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
            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = false;

                _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        messageQueue.Subscribers.TryUse(s =>
                        {
                            success = true;
                            s.Remove(connectionId);
                        });
                    }
                });

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
        internal void EnqueueMessage(string queueName, string objectType, string messageJson)
        {
            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = false;
                _messageQueues.TryUse(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        messageQueue.EnqueuedMessages.TryUse(m =>
                        {
                            success = true;

                            messageQueue.TotalEnqueuedMessages++;
                            var message = new EnqueuedMessage(queueKey, objectType, messageJson);
                            if (_persistenceDatabase != null)
                            {
                                var persistedJson = JsonConvert.SerializeObject(message);
                                lock (_persistenceDatabase)
                                {
                                    _persistenceDatabase.Put($"{queueKey}_{message.MessageId}", persistedJson);
                                }
                            }

                            m.Add(message);
                            messageQueue.DeliveryThreadWaitEvent.Set();
                        });
                    }
                    else
                    {
                        throw new Exception($"Queue not found: [{queueName}].");
                    }
                });

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
            while (true)
            {
                bool success = false;
                _messageQueues.TryUse(mqd =>
                {
                    string queueKey = queueName.ToLowerInvariant();
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        messageQueue.EnqueuedMessages.TryUse(m =>
                        {
                            success = true;
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
                        });
                    }
                    else
                    {
                        throw new Exception($"Queue not found: [{queueName}].");
                    }
                });

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
