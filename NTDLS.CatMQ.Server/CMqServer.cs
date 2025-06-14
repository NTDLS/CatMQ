﻿using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Server.Server;
using NTDLS.CatMQ.Server.Server.QueryHandlers;
using NTDLS.CatMQ.Shared;
using NTDLS.CatMQ.Shared.Payload.ServerToClient;
using NTDLS.ReliableMessaging;
using NTDLS.Semaphore;
using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;

namespace NTDLS.CatMQ.Server
{
    /// <summary>
    /// Listens for connections from MessageClients and processes the incoming notifications/queries.
    /// </summary>
    public class CMqServer
        : IDisposable
    {
        private bool _keepRunning = false;
        private readonly CMqServerConfiguration _configuration;
        private readonly JsonSerializerOptions _indentedJsonOptions = new() { WriteIndented = true };
        private readonly OptimisticCriticalResource<MessageQueueDictionary> _messageQueues = new();
        private readonly RmServer _rmServer;

        internal CMqServerConfiguration Configuration => _configuration;

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
            //ThreadLockOwnershipTracking.Enable();

            _configuration = configuration;

            var rmConfiguration = new RmConfiguration()
            {
                InitialReceiveBufferSize = _configuration.InitialReceiveBufferSize,
                MaxReceiveBufferSize = _configuration.MaxReceiveBufferSize,
                QueryTimeout = TimeSpan.FromSeconds(_configuration.AcknowledgmentTimeoutSeconds),
                ReceiveBufferGrowthRate = _configuration.ReceiveBufferGrowthRate,
                CompressionProvider = new RmDeflateCompressionProvider(),
            };

            _rmServer = new RmServer(rmConfiguration);
            _rmServer.OnException += RmServer_OnException;
            _rmServer.OnDisconnected += RmServer_OnDisconnected;
            _rmServer.AddHandler(new InternalServerQueryHandlers(this));
        }

        /// <summary>
        /// Creates a new instance of the queue service.
        /// </summary>
        public CMqServer()
        {
            _configuration = new CMqServerConfiguration();

            var rmConfiguration = new RmConfiguration()
            {
                InitialReceiveBufferSize = _configuration.InitialReceiveBufferSize,
                MaxReceiveBufferSize = _configuration.MaxReceiveBufferSize,
                QueryTimeout = TimeSpan.FromSeconds(_configuration.AcknowledgmentTimeoutSeconds),
                ReceiveBufferGrowthRate = _configuration.ReceiveBufferGrowthRate,
                CompressionProvider = new RmDeflateCompressionProvider(),
            };

            _rmServer = new RmServer(rmConfiguration);
            _rmServer.OnException += RmServer_OnException;
            _rmServer.OnDisconnected += RmServer_OnDisconnected;
            _rmServer.AddHandler(new InternalServerQueryHandlers(this));
        }

        private void RmServer_OnException(RmContext? context, Exception ex, IRmPayload? payload)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Error, "Reliable messaging exception.", ex);
        }

        internal void InvokeOnLog(Exception ex)
        {
            if (ex.Message.Equals("The connection was terminated.", StringComparison.OrdinalIgnoreCase))
            {
                //This is not really an error.
                OnLog?.Invoke(this, CMqErrorLevel.Warning, ex.Message);
            }
            else if (ex.Message.StartsWith("Connection with id", StringComparison.OrdinalIgnoreCase)
                && ex.Message.EndsWith("was not found.", StringComparison.OrdinalIgnoreCase))
            {
                //This is not really an error.
                OnLog?.Invoke(this, CMqErrorLevel.Warning, ex.Message);
            }
            else
            {
                OnLog?.Invoke(this, CMqErrorLevel.Error, ex.Message, ex);
            }
        }

        internal void InvokeOnLog(CMqErrorLevel errorLevel, string message)
            => OnLog?.Invoke(this, errorLevel, message);

        private void RmServer_OnDisconnected(RmContext context)
        {
            while (_keepRunning)
            {
                bool success = true;

                //When a client disconnects, remove their subscriptions.
                _messageQueues.Read(mqd =>
                {
                    foreach (var mqKVP in mqd)
                    {
                        success = mqKVP.Value.Subscribers.TryWrite(CMqDefaults.DEFAULT_TRY_WAIT_MS, s =>
                        {
                            s.Remove(context.ConnectionId);
                        }) && success;
                        if (!success)
                        {
                            break;
                        }
                    }
                });

                if (success)
                {
                    return;
                }
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        #region Management.

        /// <summary>
        /// Saves persistent message queues and their statistics to disk.
        /// </summary>
        public void CheckpointPersistentMessageQueues()
        {
            _messageQueues.Read(mqd => CheckpointPersistentMessageQueues(mqd));
        }

        private void CheckpointPersistentMessageQueues(MessageQueueDictionary mqd)
        {
            if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
            {
                OnLog?.Invoke(this, CMqErrorLevel.Verbose, "Checkpoint persistent queues.");

                var queueMetas = mqd.Where(q => q.Value.Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                    .Select(q => new MessageQueueMetadata(q.Value.Configuration, q.Value.Statistics)).ToList();

                var persistedQueuesJson = JsonSerializer.Serialize(queueMetas, _indentedJsonOptions);
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
        public ReadOnlyCollection<CMqQueueDescriptor>? GetQueues()
        {
            while (_keepRunning)
            {
                bool success = true;
                List<CMqQueueDescriptor>? result = new();

                _messageQueues.Read(mqd =>
                {
                    foreach (var mqKVP in mqd)
                    {
                        success = mqKVP.Value.Subscribers.TryRead(sKVP =>
                        {
                            result.Add(new CMqQueueDescriptor
                            {
                                ConsumptionScheme = mqKVP.Value.Configuration.ConsumptionScheme,
                                DeliveryScheme = mqKVP.Value.Configuration.DeliveryScheme,
                                DeliveryThrottle = mqKVP.Value.Configuration.DeliveryThrottle,
                                MaxOutstandingDeliveries = mqKVP.Value.Configuration.MaxOutstandingDeliveries,
                                MaxDeliveryAttempts = mqKVP.Value.Configuration.MaxDeliveryAttempts,
                                MaxMessageAge = mqKVP.Value.Configuration.MaxMessageAge,
                                PersistenceScheme = mqKVP.Value.Configuration.PersistenceScheme,
                                QueueName = mqKVP.Value.Configuration.QueueName,

                                CurrentSubscriberCount = sKVP.Count,
                                QueueDepth = mqKVP.Value.Statistics.QueueDepth,

                                CurrentOutstandingDeliveries = mqKVP.Value.Statistics.OutstandingDeliveries,
                                ReceivedMessageCount = mqKVP.Value.Statistics.ReceivedMessageCount,
                                DeliveredMessageCount = mqKVP.Value.Statistics.DeliveredMessageCount,
                                FailedDeliveryCount = mqKVP.Value.Statistics.FailedDeliveryCount,
                                ExpiredMessageCount = mqKVP.Value.Statistics.ExpiredMessageCount,
                                DeferredDeliveryCount = mqKVP.Value.Statistics.DeferredDeliveryCount,
                                ExplicitDeadLetterCount = mqKVP.Value.Statistics.ExplicitDeadLetterCount,
                                ExplicitDropCount = mqKVP.Value.Statistics.ExplicitDropCount,
                            });
                        }) && success;

                        if (!success)
                        {
                            //Failed to lock, break the inner loop and try again.
                            result = null;
                            break;
                        }
                    }
                });

                if (success && result != null)
                {
                    return new ReadOnlyCollection<CMqQueueDescriptor>(result);
                }

                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS); //Failed to lock, sleep then try again.
            }

            return null;
        }

        /// <summary>
        /// Returns a single queue with the given name.
        /// </summary>
        public CMqQueueDescriptor? GetQueue(string queueName)
        {
            while (_keepRunning)
            {
                bool success = true;
                CMqQueueDescriptor? result = new();

                _messageQueues.Read(mqd =>
                {
                    var queue = mqd.FirstOrDefault(o => o.Key.Equals(queueName, StringComparison.InvariantCultureIgnoreCase)).Value;
                    if (queue != null)
                    {
                        success = queue.Subscribers.TryRead(sKVP =>
                        {
                            result = new CMqQueueDescriptor
                            {
                                ConsumptionScheme = queue.Configuration.ConsumptionScheme,
                                DeliveryScheme = queue.Configuration.DeliveryScheme,
                                DeliveryThrottle = queue.Configuration.DeliveryThrottle,
                                MaxOutstandingDeliveries = queue.Configuration.MaxOutstandingDeliveries,
                                MaxDeliveryAttempts = queue.Configuration.MaxDeliveryAttempts,
                                MaxMessageAge = queue.Configuration.MaxMessageAge,
                                PersistenceScheme = queue.Configuration.PersistenceScheme,
                                QueueName = queue.Configuration.QueueName,

                                CurrentSubscriberCount = sKVP.Count,
                                QueueDepth = queue.Statistics.QueueDepth,

                                CurrentOutstandingDeliveries = queue.Statistics.OutstandingDeliveries,
                                ReceivedMessageCount = queue.Statistics.ReceivedMessageCount,
                                DeliveredMessageCount = queue.Statistics.DeliveredMessageCount,
                                FailedDeliveryCount = queue.Statistics.FailedDeliveryCount,
                                ExpiredMessageCount = queue.Statistics.ExpiredMessageCount,
                                DeferredDeliveryCount = queue.Statistics.DeferredDeliveryCount,
                                ExplicitDeadLetterCount = queue.Statistics.ExplicitDeadLetterCount,
                                ExplicitDropCount = queue.Statistics.ExplicitDropCount,
                            };
                        }) && success;
                    }
                    else
                    {
                        //Success is true, but we didn't find the queue - so we will return null.
                    }

                    if (!success)
                    {
                        //Failed to lock, try again.
                        result = null;
                    }
                });

                if (success)
                {
                    return result;
                }

                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS); //Failed to lock, sleep then try again.
            }

            return null;
        }

        /// <summary>
        /// Returns a read-only copy of the queue subscribers.
        /// </summary>
        public ReadOnlyCollection<CMqSubscriberDescriptor>? GetSubscribers(string queueName)
        {
            while (_keepRunning)
            {
                bool success = true;
                var result = new List<CMqSubscriberDescriptor>();

                _messageQueues.Read(mqd =>
                {
                    if (mqd.TryGetValue(queueName, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryRead(CMqDefaults.DEFAULT_TRY_WAIT_MS, sKVP =>
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
                });

                if (success)
                {
                    return new ReadOnlyCollection<CMqSubscriberDescriptor>(result);
                }

                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS); //Failed to lock, sleep then try again.
            }

            return null;
        }

        /// <summary>
        /// Returns a read-only copy messages in the queue.
        /// </summary>
        public CMqEnqueuedMessageDescriptorCollection? GetQueueMessages(string queueName, int offset, int take)
        {
            while (_keepRunning)
            {
                bool success = true;
                List<CMqEnqueuedMessageDescriptor>? result = new();

                int queueDepth = 0;

                _messageQueues.Read(mqd =>
                {
                    var filteredQueues = mqd.Where(o => o.Value.Configuration.QueueName.Equals(queueName, StringComparison.OrdinalIgnoreCase));
                    foreach (var qKVP in filteredQueues)
                    {
                        success = qKVP.Value.EnqueuedMessages.TryReadAll([qKVP.Value.Subscribers], CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                        {
                            qKVP.Value.Subscribers.Read(sKVP =>
                            {
                                queueDepth = qKVP.Value.Statistics.QueueDepth;

                                if (qKVP.Value.Configuration.PersistenceScheme == CMqPersistenceScheme.Ephemeral)
                                {
                                    foreach (var message in m.MessageBuffer.Skip(offset).Take(take))
                                    {
                                        result.Add(new CMqEnqueuedMessageDescriptor(message.SerialNumber)
                                        {
                                            Timestamp = message.Timestamp,
                                            SubscriberCount = sKVP.Count,
                                            DeferredUntil = message.DeferredUntil,
                                            SubscriberMessageDeliveries = message.SubscriberMessageDeliveries.Keys.ToHashSet(),
                                            SatisfiedSubscribersSubscriberIDs = message.SatisfiedDeliverySubscriberIDs,
                                            AssemblyQualifiedTypeName = message.AssemblyQualifiedTypeName,
                                            MessageJson = message.MessageJson,
                                        });
                                    }
                                }
                                else if (m.Database != null && qKVP.Value.Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                                {
                                    int currentIndex = 0;
                                    int takenCount = 0;

                                    using var iterator = m.Database.NewIterator();
                                    iterator.SeekToFirst();

                                    while (iterator.Valid() && currentIndex < offset)
                                    {
                                        iterator.Next();
                                        currentIndex++;
                                    }

                                    while (iterator.Valid() && takenCount < take)
                                    {
                                        var persistedMessage = JsonSerializer.Deserialize<EnqueuedMessage>(iterator.StringValue());
                                        if (persistedMessage != null)
                                        {
                                            result.Add(new CMqEnqueuedMessageDescriptor(persistedMessage.SerialNumber)
                                            {
                                                Timestamp = persistedMessage.Timestamp,
                                                SubscriberCount = sKVP.Count,
                                                DeferredUntil = persistedMessage.DeferredUntil,
                                                SubscriberMessageDeliveries = persistedMessage.SubscriberMessageDeliveries.Keys.ToHashSet(),
                                                SatisfiedSubscribersSubscriberIDs = persistedMessage.SatisfiedDeliverySubscriberIDs,
                                                AssemblyQualifiedTypeName = persistedMessage.AssemblyQualifiedTypeName,
                                                MessageJson = persistedMessage.MessageJson,
                                            });
                                        }
                                        iterator.Next();
                                        takenCount++;
                                    }
                                }
                            });
                        }) && success;

                        if (!success)
                        {
                            //Failed to lock, break the inner loop and try again.
                            result = null;
                            break;
                        }
                    }
                });

                if (success && result != null)
                {
                    return new CMqEnqueuedMessageDescriptorCollection(result)
                    {
                        Offset = offset,
                        Take = take,
                        Count = result.Count,
                        QueueDepth = queueDepth
                    };
                }

                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS); //Failed to lock, sleep then try again.
            }

            return null;
        }

        /// <summary>
        /// Returns a read-only copy of a message in the queue.
        /// </summary>
        public CMqEnqueuedMessageDescriptor? GetQueueMessage(string queueName, ulong serialNumber, int? truncateToBytes = null)
        {
            while (_keepRunning)
            {
                bool success = true;
                CMqEnqueuedMessageDescriptor? result = null;

                _messageQueues.Read(mqd =>
                {
                    if (mqd.TryGetValue(queueName, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryRead(CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                        {
                            if (messageQueue.Configuration.PersistenceScheme == CMqPersistenceScheme.Ephemeral)
                            {
                                var message = m.MessageBuffer.Where(o => o.SerialNumber == serialNumber).FirstOrDefault()
                                    ?? throw new Exception($"Message not found: [{serialNumber}].");

                                result = new CMqEnqueuedMessageDescriptor(message.SerialNumber)
                                {
                                    Timestamp = message.Timestamp,
                                    SubscriberMessageDeliveries = message.SubscriberMessageDeliveries.Keys.ToHashSet(),
                                    SatisfiedSubscribersSubscriberIDs = message.SatisfiedDeliverySubscriberIDs,
                                    AssemblyQualifiedTypeName = message.AssemblyQualifiedTypeName,
                                    MessageJson = message.MessageJson,
                                };
                            }
                            else if (messageQueue.Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                            {
                                if (m.Database == null)
                                {
                                    throw new Exception($"Persistence database has not been initialized for [{queueName}].");
                                }

                                var persistedMessage = m.Database.RetrieveMessage(serialNumber)
                                    ?? throw new Exception($"Failed to deserialize message: [{serialNumber}].");

                                result = new CMqEnqueuedMessageDescriptor(persistedMessage.SerialNumber)
                                {
                                    Timestamp = persistedMessage.Timestamp,
                                    SubscriberMessageDeliveries = persistedMessage.SubscriberMessageDeliveries.Keys.ToHashSet(),
                                    SatisfiedSubscribersSubscriberIDs = persistedMessage.SatisfiedDeliverySubscriberIDs,
                                    AssemblyQualifiedTypeName = persistedMessage.AssemblyQualifiedTypeName,
                                    MessageJson = persistedMessage.MessageJson,
                                };
                            }
                        }) && success;
                    }
                    else
                    {
                        throw new Exception($"Queue not found: [{queueName}].");
                    }
                });

                if (success && result != null)
                {
                    if (truncateToBytes != null)
                    {
                        if (result.MessageJson.Length > truncateToBytes)
                        {
                            result.MessageJson = result.MessageJson.Substring(0, truncateToBytes.Value);
                        }
                    }

                    return result;
                }

                Thread.Sleep(1); //Failed to lock, sleep then try again.
            }

            return null;
        }

        #endregion

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

            var messageQueuesToLoad = new List<SingleMessageQueueServer>();
            var deadLetterQueuesToLoad = new List<SingleMessageQueueServer>();
            var queuesToStart = new List<SingleMessageQueueServer>();

            if (_configuration.PersistencePath != null)
            {
                var persistedQueuesFile = Path.Join(_configuration.PersistencePath, "queues.json");
                if (File.Exists(persistedQueuesFile))
                {
                    OnLog?.Invoke(this, CMqErrorLevel.Information, "Loading persistent queues.");

                    var persistedQueuesJson = File.ReadAllText(persistedQueuesFile);
                    var queueMetas = JsonSerializer.Deserialize<List<MessageQueueMetadata>>(persistedQueuesJson);

                    if (queueMetas != null)
                    {
                        _messageQueues.Write(mqd =>
                       {
                           foreach (var queueMeta in queueMetas)
                           {
                               var messageQueue = new SingleMessageQueueServer(this, queueMeta.Configuration)
                               {
                                   Statistics = queueMeta.Statistics
                               };
                               queuesToStart.Add(messageQueue);

                               if (messageQueue.Configuration.QueueName.EndsWith(".dlq"))
                               {
                                   deadLetterQueuesToLoad.Add(messageQueue);
                               }
                               else
                               {
                                   messageQueuesToLoad.Add(messageQueue);
                               }
                               mqd.Add(queueMeta.Configuration.QueueName.ToLowerInvariant(), messageQueue);

                               if (queueMeta.Configuration.DeadLetterConfiguration != null
                                   && queueMeta.Configuration.DeadLetterConfiguration.PersistenceScheme == CMqPersistenceScheme.Ephemeral)
                               {
                                   //Persistent DLQs are created by default, if the queue has an Ephemeral DLQ then we need to manually create it.
                                   var dlqConfig = queueMeta.Configuration.DeadLetterConfiguration.ToConfiguration(queueMeta.Configuration.QueueName);
                                   var dlq = new SingleMessageQueueServer(this, dlqConfig);
                                   queuesToStart.Add(dlq);
                                   mqd.Add(dlqConfig.QueueName.ToLowerInvariant(), dlq);
                               }
                           }
                       });
                    }
                }
            }

            //Load DLQs first, because the regular queues will need to use them when they load.
            var loadTasks = deadLetterQueuesToLoad.Select(m => Task.Run(() => m.InitializePersistentDatabase())).ToArray();
            Task.WaitAll(loadTasks);

            loadTasks = messageQueuesToLoad.Select(m => Task.Run(() => m.InitializePersistentDatabase())).ToArray();
            Task.WaitAll(loadTasks);

            OnLog?.Invoke(this, CMqErrorLevel.Information, "Starting queues.");
            foreach (var mq in queuesToStart)
            {
                mq.Start();
            }

            _rmServer.Start(listenPort);

            new Thread(() => HeartbeatThread())
            {
                IsBackground = true
            }.Start();
        }

        private void HeartbeatThread()
        {
            var lastCheckpoint = DateTime.UtcNow;

            while (_keepRunning)
            {
                if (DateTime.UtcNow - lastCheckpoint > TimeSpan.FromSeconds(30))
                {
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
            OnLog?.Invoke(this, CMqErrorLevel.Information, "Stopping reliable messaging.");
            _rmServer.Stop();

            var messageQueues = new List<SingleMessageQueueServer>();

            _messageQueues.Read(mqd =>
            {
                //Stop all message queues.
                foreach (var mqKVP in mqd)
                {
                    OnLog?.Invoke(this, CMqErrorLevel.Information, $"Stopping queue [{mqKVP.Value.Configuration.QueueName}].");
                    mqKVP.Value.SignalShutdown();
                    messageQueues.Add(mqKVP.Value);
                }

                if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
                {
                    CheckpointPersistentMessageQueues(mqd);
                }
            });

            foreach (var messageQueue in messageQueues)
            {
                messageQueue.WaitForShutdown(); //We cant wait on the stop from within a lock. That'll deadlock.
            }
        }

        #endregion

        #region Message queue interactions.

        internal void ShovelToDeadLetter(string sourceQueueName, EnqueuedMessage givenMessage)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Dead-lettering message for [{sourceQueueName}].");

            var dlqName = $"{sourceQueueName}.dlq";
            var dlqKey = dlqName.ToLowerInvariant();

            while (_keepRunning)
            {
                bool success = true;

                _messageQueues.Read(mqd =>
                {
                    if (mqd.TryGetValue(dlqKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryWrite(CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                        {
                            //Yes, DLQ messages get a new serial number, its a different queue after all.
                            var message = givenMessage.CloneForDeadLetter(dlqName, messageQueue.Statistics.GetNextSerialNumber());

                            messageQueue.Statistics.IncrementReceivedMessageCount();
                            messageQueue.Statistics.IncrementQueueDepth();

                            if (messageQueue.Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                            {
                                if (m.Database == null)
                                {
                                    throw new Exception($"Persistence database has not been initialized for [{messageQueue.Configuration.QueueName}].");
                                }

                                m.Database.Store(message);
                                //For persistent queues, the messages are only loaded into the database.
                                //They will be buffered into the message buffer by the message queue delivery thread.
                            }
                            else
                            {
                                //We have to keep all ephemeral messages in memory.
                                m.MessageBuffer.Add(message);
                            }

                            messageQueue.DeliveryThreadWaitEvent.Set();
                        }) && success;
                    }
                    else
                    {
                        OnLog?.Invoke(this, CMqErrorLevel.Warning, $"Dead-letter queue does not exist, discarding message for [{sourceQueueName}].");
                    }
                });

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
        internal async Task<CMqConsumeResult> DeliverMessageWithResult(Guid subscriberId, string queueName, EnqueuedMessage enqueuedMessage)
        {
            var message = new CMqMessageDeliveryQuery(queueName, enqueuedMessage.SerialNumber,
                enqueuedMessage.AssemblyQualifiedTypeName, enqueuedMessage.MessageJson)
            {
                Timestamp = enqueuedMessage.Timestamp,
                DeferDuration = enqueuedMessage.DeferDuration,
                DeferredCount = enqueuedMessage.DeferredCount,
                SubscriberDeliveryCount = enqueuedMessage.SubscriberMessageDeliveries.Count,
                SatisfiedSubscriberCount = enqueuedMessage.SatisfiedDeliverySubscriberIDs.Count,
                FailedSubscriberCount = enqueuedMessage.DeliveryLimitReachedSubscriberIDs.Count
            };

            var result = await _rmServer.QueryAsync(subscriberId, message);
            if (string.IsNullOrEmpty(result.ErrorMessage) == false)
            {
                throw new Exception(result.ErrorMessage);
            }
            return result.ConsumeResult;
        }

        #endregion

        #region Client interactions.

        /// <summary>
        /// Creates a new empty queue if it does not already exist.
        /// </summary>
        public void CreateQueue(CMqQueueConfiguration queueConfiguration)
        {
            if (queueConfiguration.IsDeadLetter)
            {
                throw new Exception("Dead-letter queues cannot be directly created. Use CMqQueueConfiguration.DeadLetterConfiguration instead.");
            }
            if (queueConfiguration.QueueName.EndsWith(".dlq", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new Exception("Queue names cannot end with '.dlq'.");
            }

            CreateQueueInternal(queueConfiguration);
        }

        /// <summary>
        /// Creates a new empty queue if it does not already exist.
        /// </summary>
        private void CreateQueueInternal(CMqQueueConfiguration queueConfiguration)
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
                    var messageQueue = new SingleMessageQueueServer(this, queueConfiguration);
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
                        var dlqConfig = queueConfiguration.DeadLetterConfiguration.ToConfiguration(queueConfiguration.QueueName);
                        CreateQueueInternal(dlqConfig);
                    }

                    messageQueue.InitializePersistentDatabase();
                    messageQueue.Start();
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

            while (_keepRunning)
            {
                bool success = true;

                SingleMessageQueueServer? waitOnStopMessageQueue = null;

                _messageQueues.Write(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryWrite(CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                        {
                            waitOnStopMessageQueue = messageQueue;
                            messageQueue.SignalShutdown();
                            mqd.Remove(queueKey);
                        }) && success;

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
                    if (waitOnStopMessageQueue != null)
                    {
                        waitOnStopMessageQueue.WaitForShutdown(); //We cant wait on the stop from within a lock. That'll deadlock.
                        var databasePath = Path.Join(Configuration.PersistencePath, "messages", waitOnStopMessageQueue.Configuration.QueueName);

                        try
                        {
                            Directory.Delete(databasePath, true);
                        }
                        catch (Exception ex)
                        {
                            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Failed to delete persisted queue messages for [{queueName}].", ex);
                        }
                    }


                    return;
                }
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        /// <summary>
        /// Creates a subscription to a queue for a given connection id.
        /// </summary>
        internal void Subscribe(Guid subscriberId, IPEndPoint? localEndpoint, IPEndPoint? remoteEndpoint, string queueName)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Subscribing connection [{subscriberId}] to queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (_keepRunning)
            {
                bool success = true;

                _messageQueues.Read(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryWrite(CMqDefaults.DEFAULT_TRY_WAIT_MS, s =>
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
                });

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
        public void Unsubscribe(Guid subscriberId, string queueName)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Unsubscribing connection [{subscriberId}] from queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (_keepRunning)
            {
                bool success = true;

                _messageQueues.Read(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryWrite(CMqDefaults.DEFAULT_TRY_WAIT_MS, s =>
                        {
                            s.Remove(subscriberId);
                        }) && success;
                    }
                });

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
        public void Enqueue(string queueName, TimeSpan? deferDeliveryDuration, string assemblyQualifiedTypeName, string messageJson)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Enqueuing message to queue: [{queueName}].");

            string queueKey = queueName.ToLowerInvariant();

            while (_keepRunning)
            {
                bool success = true;

                _messageQueues.Read(mqd =>
                {
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryWrite(CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                        {
                            messageQueue.Statistics.IncrementReceivedMessageCount();
                            messageQueue.Statistics.IncrementQueueDepth();

                            var message = new EnqueuedMessage(queueKey, assemblyQualifiedTypeName, messageJson, messageQueue.Statistics.GetNextSerialNumber())
                            {
                                DeferDuration = deferDeliveryDuration,
                                DeferredUntil = deferDeliveryDuration == null ? null : DateTime.UtcNow + deferDeliveryDuration
                            };

                            if (messageQueue.Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                            {
                                if (m.Database == null)
                                {
                                    throw new Exception($"Persistence database has not been initialized for [{queueName}].");
                                }

                                m.Database.Store(message);
                                //For persistent queues, the messages are only loaded into the database.
                                //They will be buffered into the message buffer by the message queue delivery thread.
                            }
                            else
                            {
                                //We have to keep all ephemeral messages in memory.
                                m.MessageBuffer.Add(message);
                            }
                            messageQueue.DeliveryThreadWaitEvent.Set();
                        }) && success;
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
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        /// <summary>
        /// Removes all messages from the given queue.
        /// </summary>
        public void PurgeQueue(string queueName)
        {
            OnLog?.Invoke(this, CMqErrorLevel.Verbose, $"Purging queue: [{queueName}].");

            while (_keepRunning)
            {
                bool success = true;

                _messageQueues.Read(mqd =>
                {
                    string queueKey = queueName.ToLowerInvariant();
                    if (mqd.TryGetValue(queueKey, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryWrite(CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                        {
                            if (messageQueue.Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                            {
                                if (m.Database == null)
                                {
                                    throw new Exception($"Persistence database has not been initialized for [{queueName}].");
                                }

                                m.Database.Purge();
                            }
                            m.MessageBuffer.Clear();
                            messageQueue.Statistics.SetQueueDepth(0);
                        }) && success;
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
                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
            }
        }

        /// <summary>
        /// Stops the message queue server.
        /// This does not need to be called if Stop() is called.
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        #endregion
    }
}
