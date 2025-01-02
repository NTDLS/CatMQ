using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Server.Server;
using NTDLS.CatMQ.Server.Server.QueryHandlers;
using NTDLS.CatMQ.Shared;
using NTDLS.CatMQ.Shared.Payload.ServerToClient;
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
        private readonly RmServer _rmServer;
        private readonly OptimisticCriticalResource<CaseInsensitiveMessageQueueDictionary> _messageQueues = new();
        private readonly CMqServerConfiguration _configuration;
        private RocksDb? _persistenceDatabase;
        private readonly object _persistenceDatabaseLock = new();
        private bool _keepRunning = false;

        /// <summary>
        /// Delegate used to notify of queue server exceptions.
        /// </summary>
        public delegate void OnLogEvent(CMqServer server, CMqErrorLevel errorLevel, string message, Exception? ex = null);

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
                AsynchronousQueryWaiting = configuration.AsynchronousAcknowledgment,
                InitialReceiveBufferSize = configuration.InitialReceiveBufferSize,
                MaxReceiveBufferSize = configuration.MaxReceiveBufferSize,
                QueryTimeout = TimeSpan.FromSeconds(configuration.AcknowledgmentTimeoutSeconds),
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
            _messageQueues.Read(mqd => CheckpointPersistentMessageQueues(mqd));
        }

        private void CheckpointPersistentMessageQueues(CaseInsensitiveMessageQueueDictionary mqd)
        {
            if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
            {
                OnLog?.Invoke(this, CMqErrorLevel.Verbose, "Checkpoint persistent queues.");

                var persistedQueues = mqd.Where(q => q.Value.QueueConfiguration.PersistenceScheme
                        == CMqPersistenceScheme.Persistent).Select(q => q.Value).ToList();

                //Serialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                var persistedQueuesJson = JsonSerializer.Serialize(persistedQueues);
                File.WriteAllText(Path.Join(_configuration.PersistencePath, "queues.json"), persistedQueuesJson);
            }
        }

        /// <summary>
        /// Returns a read-only copy of the running configuration.
        /// </summary>
        /// <returns></returns>
        public CMqServerDescriptor GetConfiguration()
        {
            return new CMqServerDescriptor
            {
                AsynchronousAcknowledgment = _configuration.AsynchronousAcknowledgment,
                AcknowledgmentTimeoutSeconds = _configuration.AcknowledgmentTimeoutSeconds,
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
        public ReadOnlyCollection<CMqQueueDescriptor> GetQueues()
        {
            while (true)
            {
                bool success = true;
                List<CMqQueueDescriptor>? result = new();

                success = _messageQueues.TryRead(mqd =>
                {
                    foreach (var mqKVP in mqd)
                    {
                        success = mqKVP.Value.EnqueuedMessages.TryRead(m =>
                        {
                            success = mqKVP.Value.Subscribers.TryRead(sKVP =>
                            {
                                result.Add(new CMqQueueDescriptor
                                {
                                    ConsumptionScheme = mqKVP.Value.QueueConfiguration.ConsumptionScheme,
                                    DeliveryScheme = mqKVP.Value.QueueConfiguration.DeliveryScheme,
                                    DeliveryThrottle = mqKVP.Value.QueueConfiguration.DeliveryThrottle,
                                    MaxDeliveryAttempts = mqKVP.Value.QueueConfiguration.MaxDeliveryAttempts,
                                    MaxMessageAge = mqKVP.Value.QueueConfiguration.MaxMessageAge,
                                    PersistenceScheme = mqKVP.Value.QueueConfiguration.PersistenceScheme,
                                    QueueName = mqKVP.Value.QueueConfiguration.QueueName,

                                    CurrentSubscriberCount = sKVP.Count,
                                    CurrentMessageCount = m.Count,

                                    ReceivedMessageCount = mqKVP.Value.ReceivedMessageCount,
                                    DeliveredMessageCount = mqKVP.Value.DeliveredMessageCount,
                                    DeliveryFailureCount = mqKVP.Value.DeliveryFailureCount,
                                    ExpiredMessageCount = mqKVP.Value.ExpiredMessageCount
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
                    return new ReadOnlyCollection<CMqQueueDescriptor>(result);
                }

                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy of the queue subscribers.
        /// </summary>
        public ReadOnlyCollection<CMqSubscriberDescriptor> GetSubscribers(string queueName)
        {
            while (true)
            {
                bool success = true;
                var result = new List<CMqSubscriberDescriptor>();

                success = _messageQueues.TryRead(mqd =>
                {
                    if (mqd.TryGetValue(queueName, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryRead(sKVP =>
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
                    return new ReadOnlyCollection<CMqSubscriberDescriptor>(result);
                }

                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy messages in the queue.
        /// </summary>
        public ReadOnlyCollection<CMqEnqueuedMessageDescriptor> GetQueueMessages(string queueName, int offset, int take)
        {
            while (true)
            {
                bool success = true;
                List<CMqEnqueuedMessageDescriptor>? result = new();

                success = _messageQueues.TryRead(mqd =>
                {
                    var filteredQueues = mqd.Where(o => o.Value.QueueConfiguration.QueueName.Equals(queueName, StringComparison.OrdinalIgnoreCase));
                    foreach (var qKVP in filteredQueues)
                    {
                        success = qKVP.Value.EnqueuedMessages.TryRead(m =>
                        {
                            success = qKVP.Value.Subscribers.TryRead(sKVP =>
                            {
                                foreach (var message in m.Skip(offset).Take(take))
                                {
                                    result.Add(new CMqEnqueuedMessageDescriptor
                                    {
                                        Timestamp = message.Timestamp,
                                        SubscriberCount = sKVP.Count,
                                        SubscriberMessageDeliveries = message.SubscriberMessageDeliveries.Keys.ToHashSet(),
                                        SatisfiedSubscribersSubscriberIDs = message.SatisfiedSubscribersSubscriberIDs,
                                        AssemblyQualifiedTypeName = message.AssemblyQualifiedTypeName,
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
                    return new ReadOnlyCollection<CMqEnqueuedMessageDescriptor>(result);
                }

                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy messages in the queue.
        /// </summary>
        public CMqEnqueuedMessageDescriptor GetQueueMessage(string queueName, Guid messageId)
        {
            while (true)
            {
                bool success = true;
                CMqEnqueuedMessageDescriptor? result = null;

                success = _messageQueues.TryRead(mqd =>
                {
                    if (mqd.TryGetValue(queueName, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryRead(m =>
                        {
                            var message = m.Where(o => o.MessageId == messageId).FirstOrDefault();
                            if (message != null)
                            {
                                result = new CMqEnqueuedMessageDescriptor
                                {
                                    Timestamp = message.Timestamp,
                                    SubscriberMessageDeliveries = message.SubscriberMessageDeliveries.Keys.ToHashSet(),
                                    SatisfiedSubscribersSubscriberIDs = message.SatisfiedSubscribersSubscriberIDs,
                                    AssemblyQualifiedTypeName = message.AssemblyQualifiedTypeName,
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
            => OnLog?.Invoke(server, CMqErrorLevel.Error, ex.Message, ex);

        internal void InvokeOnLog(CMqServer server, CMqErrorLevel errorLevel, string message)
            => OnLog?.Invoke(server, errorLevel, message);

        private void RmServer_OnDisconnected(RmContext context)
        {
            while (true)
            {
                bool success = true;

                //When a client disconnects, remove their subscriptions.
                success = _messageQueues.TryRead(mqd =>
                {
                    foreach (var mqKVP in mqd)
                    {
                        success = mqKVP.Value.Subscribers.TryWrite(s =>
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
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
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

            var persistedQueues = new List<MessageQueue>();
            var deadLetterQueueMessages = new Dictionary<string, List<EnqueuedMessage>>(StringComparer.OrdinalIgnoreCase);

            if (_configuration.PersistencePath != null)
            {
                #region Load and create persisted queues.

                var persistedQueuesFile = Path.Join(_configuration.PersistencePath, "queues.json");
                if (File.Exists(persistedQueuesFile))
                {
                    OnLog?.Invoke(this, CMqErrorLevel.Information, "Loading persistent queues.");

                    var persistedQueuesJson = File.ReadAllText(persistedQueuesFile);
                    //Deserialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                    var loadedPersistedQueues = JsonSerializer.Deserialize<List<MessageQueue>>(persistedQueuesJson);

                    if (loadedPersistedQueues != null)
                    {
                        _messageQueues.Write(mqd =>
                        {
                            foreach (var persistedQueue in loadedPersistedQueues)
                            {
                                persistedQueue.SetServer(this);
                                persistedQueues.Add(persistedQueue);

                                var queueKey = persistedQueue.QueueConfiguration.QueueName.ToLower();
                                if (mqd.ContainsKey(queueKey) == false)
                                {
                                    mqd.Add(queueKey, persistedQueue);
                                }
                            }
                        });
                    }
                }

                #endregion

                #region Load persisted messages.

                RocksDb? persistenceDatabase = null;

                var databaseFilePath = Path.Join(_configuration.PersistencePath, "messages");
                OnLog?.Invoke(this, CMqErrorLevel.Information, "Instantiating persistent database.");

                var options = new DbOptions()
                    .SetCreateIfMissing(true);
                persistenceDatabase = RocksDb.Open(options, databaseFilePath);

                if (persistedQueues.Count > 0)
                {
                    OnLog?.Invoke(this, CMqErrorLevel.Information, "Loading persistent messages.");

                    using var iterator = persistenceDatabase.NewIterator();
                    _messageQueues.Write(mqd =>
                    {
                        //The keys in RocksDB are not stored in the order they were added, so we
                        // need to load all messages into the messages queues then sort them in place.
                        for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                        {
                            //Deserialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                            var persistedMessage = JsonSerializer.Deserialize<EnqueuedMessage>(iterator.StringValue());
                            if (persistedMessage != null)
                            {
                                if (mqd.TryGetValue(persistedMessage.QueueName, out var messageQueue))
                                {
                                    //If we have a MaxMessageAge, check it and if the message is expired, either
                                    //  dead-letter or discard it instead of adding it to its original queue.
                                    if (messageQueue.QueueConfiguration.MaxMessageAge > TimeSpan.Zero)
                                    {
                                        if ((DateTime.UtcNow - persistedMessage.Timestamp) > messageQueue.QueueConfiguration.MaxMessageAge)
                                        {
                                            if (messageQueue.QueueConfiguration.DeadLetterConfiguration != null)
                                            {
                                                if ((DateTime.UtcNow - persistedMessage.Timestamp) > messageQueue.QueueConfiguration.DeadLetterConfiguration.MaxMessageAge)
                                                {
                                                    //Message is even too old for the dead-letter queue, discard expired message.
                                                }
                                                else if (deadLetterQueueMessages.TryGetValue(persistedMessage.QueueName, out var deadLetterMessages))
                                                {
                                                    //Add to dead-letter queue.
                                                    deadLetterMessages.Add(persistedMessage);
                                                }
                                                else
                                                {
                                                    //Add to dead-letter queue.
                                                    deadLetterQueueMessages.Add(persistedMessage.QueueName, new List<EnqueuedMessage> { persistedMessage });
                                                }
                                            }
                                            else
                                            {
                                                //No dead-letter queue, discard expired message.
                                            }
                                        }
                                        else
                                        {
                                            //Add the message back to its original queue.
                                            messageQueue.EnqueuedMessages.Write(m => m.Add(persistedMessage));
                                        }
                                    }
                                    else
                                    {
                                        //Add the message back to its original queue.
                                        messageQueue.EnqueuedMessages.Write(m => m.Add(persistedMessage));
                                    }
                                }
                            }
                        }

                        OnLog?.Invoke(this, CMqErrorLevel.Information, "Sorting loaded queues.");

                        //Sort the message in the queues by their timestamps.
                        OnLog?.Invoke(this, CMqErrorLevel.Information, "Sorting persistent messages.");
                        var tasks = mqd.Values.Select(mq => Task.Run(() => mq.SortMessages()));
                        Task.WhenAll(tasks).Wait();
                    });
                }

                #endregion

                _persistenceDatabase = persistenceDatabase;
            }

            foreach (var mq in persistedQueues)
            {
                if (deadLetterQueueMessages.TryGetValue(mq.QueueConfiguration.QueueName, out var deadLetterMessages))
                {
                    OnLog?.Invoke(this, CMqErrorLevel.Information, $"Dead-lettering {deadLetterMessages.Count:n0} expired messages in [{mq.QueueConfiguration.QueueName}].");

                    foreach (var deadLetterMessage in deadLetterMessages)
                    {
                        mq.ExpiredMessageCount++;
                        ShovelToDeadLetter(mq.QueueConfiguration.QueueName, deadLetterMessage);
                        RemovePersistenceMessage(mq.QueueConfiguration.QueueName, deadLetterMessage.MessageId);
                    }
                }
            }

            OnLog?.Invoke(this, CMqErrorLevel.Information, "Starting queues.");
            foreach (var mq in persistedQueues)
            {
                mq.StartAsync();
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
                    OnLog?.Invoke(this, CMqErrorLevel.Verbose, "Checkpoint persistent database.");
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
            OnLog?.Invoke(this, CMqErrorLevel.Information, "Stopping service.");

            _keepRunning = false;
            OnLog?.Invoke(this, CMqErrorLevel.Information, "Disposing database instance.");
            if (_persistenceDatabase != null)
            {
                lock (_persistenceDatabaseLock)
                {
                    _persistenceDatabase?.Dispose();
                    _persistenceDatabase = null;
                }
            }
            OnLog?.Invoke(this, CMqErrorLevel.Information, "Stopping reliable messaging.");
            _rmServer.Stop();

            var messageQueues = new List<MessageQueue>();

            _messageQueues.Read(mqd =>
            {
                //Stop all message queues.
                foreach (var mqKVP in mqd)
                {
                    OnLog?.Invoke(this, CMqErrorLevel.Information, $"Stopping queue [{mqKVP.Value.QueueConfiguration.QueueName}].");
                    mqKVP.Value.StopAsync();
                    messageQueues.Add(mqKVP.Value);
                }

                if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
                {
                    CheckpointPersistentMessageQueues(mqd);
                }
            });

            foreach (var messageQueue in messageQueues)
            {
                messageQueue.WaitOnStop(); //We cant wait on the stop from within a lock. That'll deadlock.
            }
        }

        #endregion

        #region Message queue interactions.

        internal void ShovelToDeadLetter(string sourceQueueName, EnqueuedMessage message)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Dead-lettering message for [{sourceQueueName}].");

            var dlqName = $"{sourceQueueName}.dlq";
            var dlqKey = dlqName.ToLowerInvariant();

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryRead(mqd =>
                {
                    if (mqd.TryGetValue(dlqKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryWrite(m =>
                        {
                            message.QueueName = dlqName; //Ne sure to change the queue name to the DLQ name.
                            message.SubscriberMessageDeliveries.Clear();
                            message.SatisfiedSubscribersSubscriberIDs.Clear();
                            message.FailedSubscribersSubscriberIDs.Clear();

                            messageQueue.ReceivedMessageCount++;
                            if (messageQueue.QueueConfiguration.PersistenceScheme == CMqPersistenceScheme.Persistent && _persistenceDatabase != null)
                            {
                                //Serialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                                var persistedJson = JsonSerializer.Serialize(message);
                                lock (_persistenceDatabaseLock)
                                {
                                    _persistenceDatabase?.Put($"{dlqKey}_{message.MessageId}", persistedJson);
                                }
                            }

                            m.Add(message);

                            messageQueue.DeliveryThreadWaitEvent.Set();
                        }) && success;
                    }
                    else
                    {
                        OnLog?.Invoke(this, CMqErrorLevel.Warning, $"Dead-letter queue does not exist, discarding message for [{sourceQueueName}].");
                    }
                }) && success;

                if (success)
                {
                    return;
                }
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        /// <summary>
        /// Deliver a message from a server queue to a subscribed client.
        /// </summary>
        internal bool DeliverMessage(Guid subscriberId, string queueName, EnqueuedMessage enqueuedMessage)
        {
            var result = _rmServer.Query(subscriberId, new CMqMessageDeliveryQuery(queueName, enqueuedMessage.AssemblyQualifiedTypeName, enqueuedMessage.MessageJson)).Result;
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
                OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Removing persistent message from [{queueName}]: [{messageId}].");
                string queueKey = queueName.ToLowerInvariant();
                lock (_persistenceDatabaseLock)
                {
                    _persistenceDatabase?.Remove($"{queueKey}_{messageId}");
                }
            }
        }

        #endregion

        #region Client interactions.

        /// <summary>
        /// Creates a new empty queue if it does not already exist.
        /// </summary>
        public void CreateQueue(CMqQueueConfiguration queueConfiguration)
        {
            if (string.IsNullOrEmpty(queueConfiguration.QueueName))
            {
                throw new Exception("A queue name is required.");
            }

            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Creating queue: [{queueConfiguration.QueueName}].");

            _messageQueues.Write(mqd =>
            {
                string queueKey = queueConfiguration.QueueName.ToLowerInvariant();
                if (mqd.ContainsKey(queueKey) == false)
                {
                    var messageQueue = new MessageQueue(this, queueConfiguration);
                    mqd.Add(queueKey, messageQueue);

                    if (queueConfiguration.PersistenceScheme == CMqPersistenceScheme.Persistent)
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

                    if (queueConfiguration.DeadLetterConfiguration != null)
                    {
                        var dlqConfiguration = new CMqQueueConfiguration($"{queueConfiguration.QueueName}.dlq")
                        {
                            DeadLetterConfiguration = null,
                            ConsumptionScheme = queueConfiguration.DeadLetterConfiguration.ConsumptionScheme,
                            MaxMessageAge = queueConfiguration.DeadLetterConfiguration.MaxMessageAge,
                            PersistenceScheme = queueConfiguration.DeadLetterConfiguration.PersistenceScheme,
                            MaxDeliveryAttempts = queueConfiguration.DeadLetterConfiguration.MaxDeliveryAttempts,
                            DeliveryScheme = queueConfiguration.DeadLetterConfiguration.DeliveryScheme,
                            DeliveryThrottle = queueConfiguration.DeadLetterConfiguration.DeliveryThrottle,
                        };
                        CreateQueue(dlqConfiguration);
                    }

                    messageQueue.StartAsync();
                }
            });
        }

        /// <summary>
        /// Deletes an existing queue.
        /// </summary>
        public void DeleteQueue(string queueName)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Deleting queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = true;

                MessageQueue? waitOnStopMessageQueue = null;

                success = _messageQueues.TryWrite(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryWrite(m =>
                        {
                            waitOnStopMessageQueue = messageQueue;
                            messageQueue.StopAsync();
                            mqd.Remove(queueKey);

                            if (messageQueue.QueueConfiguration.PersistenceScheme == CMqPersistenceScheme.Persistent && _persistenceDatabase != null)
                            {
                                foreach (var message in m)
                                {
                                    lock (_persistenceDatabaseLock)
                                    {
                                        _persistenceDatabase?.Remove($"{queueKey}_{message.MessageId}");
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
                    waitOnStopMessageQueue?.WaitOnStop(); //We cant wait on the stop from within a lock. That'll deadlock.
                    return;
                }
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        /// <summary>
        /// Creates a subscription to a queue for a given connection id.
        /// </summary>
        internal void SubscribeToQueue(Guid subscriberId, IPEndPoint? localEndpoint, IPEndPoint? remoteEndpoint, string queueName)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Subscribing connection [{subscriberId}] to queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryRead(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryWrite(s =>
                        {
                            if (s.ContainsKey(subscriberId) == false)
                            {
                                s.Add(subscriberId, new CMqSubscriberDescriptor(subscriberId)
                                {
                                    LocalAddress = localEndpoint?.Address?.ToString(),
                                    RemoteAddress = remoteEndpoint?.Address?.ToString(),
                                    LocalPort = localEndpoint?.Port,
                                    RemotePort = remoteEndpoint?.Port
                                });
                            }
                        }) && success;
                    }
                    else
                    {
                        throw new Exception("The specified queue does not exist.");
                    }
                }) && success;

                if (success)
                {
                    return;
                }
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        /// <summary>
        /// Removes a subscription from a queue for a given connection id.
        /// </summary>
        public void UnsubscribeFromQueue(Guid subscriberId, string queueName)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Unsubscribing connection [{subscriberId}] from queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryRead(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryWrite(s =>
                        {
                            s.Remove(subscriberId);
                        }) && success;
                    }
                }) && success;

                if (success)
                {
                    return;
                }
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        /// <summary>
        /// Removes a subscription from a queue for a given connection id.
        /// </summary>
        public void EnqueueMessage(string queueName, string assemblyQualifiedTypeName, string messageJson)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Enqueuing message to queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryRead(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryWrite(m =>
                        {
                            messageQueue.ReceivedMessageCount++;
                            var message = new EnqueuedMessage(queueKey, assemblyQualifiedTypeName, messageJson);
                            if (messageQueue.QueueConfiguration.PersistenceScheme == CMqPersistenceScheme.Persistent && _persistenceDatabase != null)
                            {
                                //Serialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                                var persistedJson = JsonSerializer.Serialize(message);
                                lock (_persistenceDatabaseLock)
                                {
                                    _persistenceDatabase?.Put($"{queueKey}_{message.MessageId}", persistedJson);
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
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        /// <summary>
        /// Removes all messages from the given queue.
        /// </summary>
        public void PurgeQueue(string queueName)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Purging queue: [{queueName}].");

            while (true)
            {
                bool success = true;

                success = _messageQueues.TryRead(mqd =>
                {
                    string queueKey = queueName.ToLowerInvariant();
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryWrite(m =>
                        {
                            if (messageQueue.QueueConfiguration.PersistenceScheme == CMqPersistenceScheme.Persistent && _persistenceDatabase != null)
                            {
                                foreach (var message in m)
                                {
                                    lock (_persistenceDatabaseLock)
                                    {
                                        _persistenceDatabase?.Remove($"{queueKey}_{message.MessageId}");
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
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        #endregion
    }
}
