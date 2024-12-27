using Newtonsoft.Json;
using NTDLS.PrudentMessageQueue.Server;
using NTDLS.PrudentMessageQueue.Server.QueryHandlers;
using NTDLS.PrudentMessageQueueServer.Management;
using NTDLS.PrudentMessageQueueShared;
using NTDLS.PrudentMessageQueueShared.Payloads.Queries.ServerToClient;
using NTDLS.ReliableMessaging;
using NTDLS.Semaphore;
using RocksDbSharp;
using System.Collections.ObjectModel;
using System.Net;

namespace NTDLS.PrudentMessageQueueServer
{
    /// <summary>
    /// Listens for connections from MessageClients and processes the incoming notifications/queries.
    /// </summary>
    public class PMqServer
    {
        private readonly RmServer _rmServer;
        private readonly PessimisticCriticalResource<CaseInsensitiveMessageQueueDictionary> _messageQueues = new();
        private readonly PMqServerConfiguration _configuration;
        private RocksDb? _persistenceDatabase;

        /// <summary>
        /// Delegate used to notify of queue server exceptions.
        /// </summary>
        public delegate void OnExceptionEvent(PMqServer server, PMqQueueConfiguration? queue, Exception ex);

        /// <summary>
        /// Event used to notify of queue server exceptions.
        /// </summary>
        public event OnExceptionEvent? OnException;

        /// <summary>
        /// Creates a new instance of the queue service.
        /// </summary>
        public PMqServer(PMqServerConfiguration configuration)
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
        public PMqServer()
        {
            _configuration = new PMqServerConfiguration();
            _rmServer = new RmServer();
            _rmServer.AddHandler(new InternalServerQueryHandlers(this));
            _rmServer.OnDisconnected += RmServer_OnDisconnected;
        }

        /// <summary>
        /// Returns a read-only copy of the running configuration.
        /// </summary>
        /// <returns></returns>
        public PMqServerInformation GetConfiguration()
        {
            return new PMqServerInformation
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
        /// <returns></returns>
        public ReadOnlyCollection<PMqQueueInformation> GetQueues()
        {
            while (true)
            {
                bool success = false;
                List<PMqQueueInformation>? result = new();

                success = _messageQueues.TryUse(mq =>
                {
                    foreach (var q in mq)
                    {
                        var enqueuedMessagesSuccess = q.Value.EnqueuedMessages.TryUse(m =>
                        {
                            result.Add(new PMqQueueInformation
                            {
                                BatchDeliveryInterval = q.Value.QueueConfiguration.BatchDeliveryInterval,
                                ConsumptionScheme = q.Value.QueueConfiguration.ConsumptionScheme,
                                CurrentEnqueuedMessageCount = m.Count,
                                DeliveryScheme = q.Value.QueueConfiguration.DeliveryScheme,
                                DeliveryThrottle = q.Value.QueueConfiguration.DeliveryThrottle,
                                MaxDeliveryAttempts = q.Value.QueueConfiguration.MaxDeliveryAttempts,
                                MaxMessageAge = q.Value.QueueConfiguration.MaxMessageAge,
                                QueueName = q.Value.QueueConfiguration.QueueName,
                                TotalDeliveredMessages = q.Value.TotalDeliveredMessages,
                                TotalEnqueuedMessages = q.Value.TotalEnqueuedMessages,
                                TotalExpiredMessages = q.Value.TotalExpiredMessages
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
                    return new ReadOnlyCollection<PMqQueueInformation>(result);
                }

                Thread.Sleep(1); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy of the queue subscribers.
        /// </summary>
        /// <returns></returns>
        public ReadOnlyCollection<PMqSubscriberInformation> GetSubscribers(string queueName)
        {
            while (true)
            {
                bool success = false;
                var result = new List<PMqSubscriberInformation>();

                success = _messageQueues.TryUse(mq =>
                {
                    if (mq.TryGetValue(queueName, out var messageQueue))
                    {
                        success = messageQueue.Subscribers.TryUse(s =>
                        {
                            foreach (var subscriber in s)
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
                    return new ReadOnlyCollection<PMqSubscriberInformation>(result);
                }

                Thread.Sleep(1); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy messages in the queue.
        /// </summary>
        /// <returns></returns>
        public ReadOnlyCollection<PMqEnqueuedMessageInformation> GetQueueMessages(string queueName, int offset, int take)
        {
            while (true)
            {
                bool success = false;
                List<PMqEnqueuedMessageInformation>? result = new();

                success = _messageQueues.TryUse(mq =>
                {
                    var filteredQueues = mq.Where(o => o.Value.QueueConfiguration.QueueName.Equals(queueName, StringComparison.OrdinalIgnoreCase));
                    foreach (var q in filteredQueues)
                    {
                        var enqueuedMessagesSuccess = q.Value.EnqueuedMessages.TryUse(m =>
                        {
                            foreach (var message in m.Skip(offset).Take(take))
                            {
                                result.Add(new PMqEnqueuedMessageInformation
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
                    return new ReadOnlyCollection<PMqEnqueuedMessageInformation>(result);
                }

                Thread.Sleep(1); //Failed to lock, sleep then try again.
            }
        }

        /// <summary>
        /// Returns a read-only copy messages in the queue.
        /// </summary>
        /// <returns></returns>
        public PMqEnqueuedMessageInformation GetQueueMessage(string queueName, Guid messageId)
        {
            while (true)
            {
                bool success = false;
                PMqEnqueuedMessageInformation? result = null;

                success = _messageQueues.TryUse(mq =>
                {
                    if (mq.TryGetValue(queueName, out var messageQueue))
                    {
                        success = messageQueue.EnqueuedMessages.TryUse(m =>
                        {
                            var message = m.Where(o => o.MessageId == messageId).FirstOrDefault();
                            if (message != null)
                            {
                                result = new PMqEnqueuedMessageInformation
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

        internal void InvokeOnException(PMqServer server, PMqQueueConfiguration? queue, Exception ex)
            => OnException?.Invoke(server, queue, ex);

        private void RmServer_OnDisconnected(RmContext context)
        {
            while (true)
            {
                bool success = false;

                //When a client disconnects, remove their subscriptions.
                _messageQueues.TryUse(mq =>
                {
                    foreach (var q in mq)
                    {
                        q.Value.Subscribers.TryUse(s =>
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
                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Starts the message queue server.
        /// </summary>
        public void Start(int listenPort)
        {
            if (_configuration.PersistencePath != null)
            {
                #region Create persisted queues.

                var persistedQueuesJson = File.ReadAllText(Path.Join(_configuration.PersistencePath, "queues.json"));
                var persistedQueues = JsonConvert.DeserializeObject<List<MessageQueue>>(persistedQueuesJson);

                if (persistedQueues != null)
                {
                    _messageQueues.Use(o =>
                    {
                        foreach (var persistedQueue in persistedQueues)
                        {
                            persistedQueue.SetServer(this);
                            string queueKey = persistedQueue.QueueConfiguration.QueueName.ToLower();
                            if (o.ContainsKey(queueKey) == false)
                            {
                                o.Add(queueKey, persistedQueue);
                                persistedQueue.Start();
                            }
                        }
                    });
                }

                #endregion

                #region Load persisted messages.

                var persistedQueueMessages = new Dictionary<string, List<EnqueuedMessage>>(StringComparer.OrdinalIgnoreCase);
                RocksDb? persistenceDatabase = null;

                if (persistedQueues != null)
                {
                    string databaseFile = Path.Join(_configuration.PersistencePath, "messages");

                    var options = new DbOptions()
                        .SetCreateIfMissing(true);
                    persistenceDatabase = RocksDb.Open(options, databaseFile);

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

                if (persistedQueues != null)
                {
                    _messageQueues.Use(o =>
                    {
                        foreach (var persistedQueueMessage in persistedQueueMessages)
                        {
                            if (o.TryGetValue(persistedQueueMessage.Key, out var messageQueue))
                            {
                                messageQueue.EnqueuedMessages.Use(s =>
                                {
                                    var persistedMessages = persistedQueueMessage.Value.OrderBy(o => o.Timestamp);
                                    foreach (var persistedMessage in persistedMessages)
                                    {
                                        s.Add(persistedMessage);
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
        }

        /// <summary>
        /// Stops the message queue server.
        /// </summary>
        public void Stop()
        {
            _persistenceDatabase?.Dispose();
            _rmServer.Stop();

            _messageQueues.Use(o =>
            {
                //Stop all message queues.
                foreach (var q in o)
                {
                    q.Value.Stop();
                }

                if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
                {
                    //TODO: checkpoint this from time to time.
                    var persistedQueues = o.Where(q => q.Value.QueueConfiguration.Persistence == PMqPersistence.Persistent).Select(q => q.Value).ToList();
                    var persistedQueuesJson = JsonConvert.SerializeObject(persistedQueues);
                    File.WriteAllText(Path.Join(_configuration.PersistencePath, "queues.json"), persistedQueuesJson);
                }
            });
        }

        /// <summary>
        /// Deliver a message from a server queue to a subscribed client.
        /// </summary>
        internal bool DeliverMessage(Guid connectionId, string queueName, EnqueuedMessage enqueuedMessage)
        {
            var result = _rmServer.Query(connectionId, new PMqMessageDeliveryQuery(queueName, enqueuedMessage.ObjectType, enqueuedMessage.MessageJson)).Result;
            if (string.IsNullOrEmpty(result.ErrorMessage) == false)
            {
                throw new Exception(result.ErrorMessage);
            }
            return result.WasMessageConsumed;
        }

        internal void RemovePersistenceMessage(string queueName, Guid MessageId)
        {
            if (_persistenceDatabase != null)
            {
                string queueKey = queueName.ToLowerInvariant();
                lock (_persistenceDatabase)
                {
                    _persistenceDatabase?.Remove($"{queueKey}_{MessageId}");
                }
            }
        }

        #region Client Instructions.

        /// <summary>
        /// Creates a new empty queue if it does not already exist.
        /// </summary>
        internal void CreateQueue(PMqQueueConfiguration queueConfiguration)
        {
            _messageQueues.Use(o =>
            {
                string queueKey = queueConfiguration.QueueName.ToLowerInvariant();
                if (o.ContainsKey(queueKey) == false)
                {
                    var messageQueue = new MessageQueue(this, queueConfiguration);
                    o.Add(queueKey, messageQueue);

                    if (queueConfiguration.Persistence == PMqPersistence.Persistent)
                    {
                        if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
                        {
                            var persistedQueues = o.Where(q => q.Value.QueueConfiguration.Persistence == PMqPersistence.Persistent).Select(q => q.Value).ToList();
                            var persistedQueuesJson = JsonConvert.SerializeObject(persistedQueues);
                            File.WriteAllText(Path.Join(_configuration.PersistencePath, "queues.json"), persistedQueuesJson);
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
            while (true)
            {
                bool success = false;
                _messageQueues.TryUse(o =>
                {
                    string queueKey = queueName.ToLowerInvariant();
                    if (o.TryGetValue(queueKey, out var messageQueue))
                    {
                        messageQueue.EnqueuedMessages.TryUse(s =>
                        {
                            success = true;

                            messageQueue.Stop();
                            o.Remove(queueKey);

                            if (_persistenceDatabase != null)
                            {
                                foreach (var message in s)
                                {
                                    _persistenceDatabase?.Remove($"{queueKey}_{message.MessageId}");
                                }

                            }

                            if (string.IsNullOrEmpty(_configuration.PersistencePath) == false)
                            {
                                var persistedQueues = o.Where(q => q.Value.QueueConfiguration.Persistence == PMqPersistence.Persistent).Select(q => q.Value).ToList();
                                var persistedQueuesJson = JsonConvert.SerializeObject(persistedQueues);
                                File.WriteAllText(Path.Join(_configuration.PersistencePath, "queues.json"), persistedQueuesJson);
                            }
                        });
                    }
                });

                if (success)
                {
                    return;
                }
                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Creates a subscription to a queue for a given connection id.
        /// </summary>
        internal void SubscribeToQueue(Guid connectionId, IPEndPoint? localEndpoint, IPEndPoint? remoteEndpoint, string queueName)
        {
            while (true)
            {
                bool success = false;
                _messageQueues.TryUse(o =>
                {
                    string queueKey = queueName.ToLowerInvariant();
                    if (o.TryGetValue(queueKey, out var messageQueue))
                    {
                        messageQueue.Subscribers.TryUse(s =>
                        {
                            success = true;

                            if (s.ContainsKey(connectionId) == false)
                            {
                                s.Add(connectionId, new PMqSubscriberInformation(connectionId)
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
                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Removes a subscription from a queue for a given connection id.
        /// </summary>
        internal void UnsubscribeFromQueue(Guid connectionId, string queueName)
        {
            while (true)
            {
                bool success = false;

                _messageQueues.TryUse(o =>
                {
                    string queueKey = queueName.ToLowerInvariant();
                    if (o.TryGetValue(queueKey, out var messageQueue))
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
                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// Removes a subscription from a queue for a given connection id.
        /// </summary>
        internal void EnqueueMessage(string queueName, string objectType, string messageJson)
        {
            while (true)
            {
                bool success = false;
                _messageQueues.TryUse(o =>
                {
                    string queueKey = queueName.ToLowerInvariant();
                    if (o.TryGetValue(queueKey, out var messageQueue))
                    {
                        messageQueue.EnqueuedMessages.TryUse(s =>
                        {
                            success = true;

                            messageQueue.TotalEnqueuedMessages++;
                            var message = new EnqueuedMessage(queueKey, objectType, messageJson);
                            if (_persistenceDatabase != null)
                            {
                                var persistedJson = JsonConvert.SerializeObject(message);
                                lock (_persistenceDatabase)
                                {
                                    _persistenceDatabase?.Put($"{queueKey}_{message.MessageId}", persistedJson);
                                }
                            }

                            s.Add(message);
                        });
                        messageQueue.DeliveryThreadWaitEvent.Set();
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
                Thread.Sleep(10);
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

                _messageQueues.TryUse(o =>
                {
                    string queueKey = queueName.ToLowerInvariant();
                    if (o.TryGetValue(queueKey, out var messageQueue))
                    {
                        messageQueue.EnqueuedMessages.TryUse(s =>
                        {
                            success = true;
                            if (_persistenceDatabase != null)
                            {
                                foreach (var message in s)
                                {
                                    _persistenceDatabase?.Remove($"{queueKey}_{message.MessageId}");
                                }
                            }
                            s.Clear();
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
                Thread.Sleep(10);
            }
        }

        #endregion
    }
}
