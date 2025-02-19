﻿using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;
using NTDLS.Semaphore;
using RocksDbSharp;
using System.Text;
using System.Text.Json;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A named message queue and its delivery thread.
    /// </summary>
    internal class SingleMessageQueueServer
    {
        private readonly Thread _deliveryThread;
        private readonly CMqServer _queueServer;

        internal AutoResetEvent DeliveryThreadWaitEvent = new(false);
        internal bool KeepRunning { get; set; } = false;

        /// <summary>
        /// List of subscriber connection IDs.
        /// </summary>
        internal OptimisticCriticalResource<Dictionary<Guid, CMqSubscriberDescriptor>> Subscribers { get; set; } = new();

        /// <summary>
        /// Messages that are enqueued in this list.
        /// </summary>
        internal OptimisticCriticalResource<EnqueuedMessageContainer> EnqueuedMessages { get; set; } = new();

        internal CMqQueueConfiguration Configuration { get; set; }

        internal MessageQueueStatistics Statistics { get; set; } = new();

        public SingleMessageQueueServer(CMqServer mqServer, CMqQueueConfiguration queueConfiguration)
        {
            _queueServer = mqServer;
            Configuration = queueConfiguration;
            _deliveryThread = new(DeliveryThreadProc);
        }

        private void DeliveryThreadProc(object? p)
        {
#if DEBUG
            Thread.CurrentThread.Name = $"DeliveryThreadProc_{Environment.CurrentManagedThreadId}";
#endif
            var lastCheckpoint = DateTime.UtcNow;
            bool attemptBufferRehydration = false;

            int threadYieldBurndown = CMqDefaults.QUEUE_THREAD_DELIVERY_BURNDOWN; //Just used to omit waiting. We want to spin fast when we are delivering messages.

            while (KeepRunning)
            {
                var now = DateTime.UtcNow;

                if (DateTime.UtcNow - lastCheckpoint > TimeSpan.FromSeconds(30))
                {
                    if (Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                    {
                        EnqueuedMessages.TryWrite(1, o =>
                        {
                            if (o.Database != null)
                            {
                                //While the RockDB WAL logs data, it’s a good idea to flush the MemTable to disk periodically for additional safety.
                                _queueServer.InvokeOnLog(CMqErrorLevel.Verbose, $"Checkpoint persistent database for [{Configuration.QueueName}].");
                                o.Database.Flush(new FlushOptions());
                            }
                        });
                    }

                    lastCheckpoint = now;
                }

                try
                {
                    EnqueuedMessage? topMessage = null;

                    if (attemptBufferRehydration)
                    {
                        attemptBufferRehydration = false;
                        HydrateMessageBuffer(CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_BUFFER_SIZE);
                    }

                    EnqueuedMessages.TryReadAll([Subscribers], CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                    {
                        if (Configuration.MaxMessageAge != null)
                        {
                            //Look for and flag expired messages.
                            //We do this here so that we can process expired messages even when there are no subscribers.
                            var expiredMessages = m.MessageBuffer.Where(o =>
                                o.State == CMqMessageState.Ready
                                && (now - o.Timestamp) > Configuration.MaxMessageAge);

                            foreach (var message in expiredMessages)
                            {
                                //If MaxMessageAge is defined, then remove the stale messages.
                                if (Configuration.DeadLetterConfiguration != null)
                                {
                                    if ((now - message.Timestamp) > Configuration.DeadLetterConfiguration.MaxMessageAge)
                                    {
                                        //Even too old for the dead-letter queue, discard expired message.
                                        message.State = CMqMessageState.Drop;
                                    }
                                    else
                                    {
                                        message.State = CMqMessageState.DeadLetter;
                                    }
                                }
                            }
                        }

                        if (Statistics.OutstandingDeliveries < Configuration.MaxOutstandingDeliveries)
                        {
                            Subscribers.Read(s => //This lock is already held.
                            {
                                //We only process a queue if it has subscribers so that we do not
                                //  discard messages as delivered for queues with no subscribers.
                                if (s.Count > 0)
                                {
                                    //Get the first message in the list, if any.
                                    topMessage = m.MessageBuffer.FirstOrDefault(o =>
                                        o.State == CMqMessageState.Ready && (o.DeferredUntil == null || now >= o.DeferredUntil));

                                    if (Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                                    {
                                        //We rehydrate the queue from the database when we have a queue-depth and either we didn't
                                        //  get a top-message or we are under the minimum buffer size. We have to check for the NULL
                                        //  top-message because it could be that we do not have any qualified messages in the buffer
                                        //  (because all of them are deferred) even though we do have messages in the buffer.
                                        if (Statistics.QueueDepth > m.MessageBuffer.Count && (topMessage == null || m.MessageBuffer.Count < CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_MIN_BUFFER))
                                        {
                                            //If we have more items in the queue than we have in the buffer, then trigger a rehydrate.
                                            attemptBufferRehydration = (Statistics.QueueDepth > m.MessageBuffer.Count);
                                        }
                                    }
                                }
                            });
                        }
                    });

                    if (topMessage != null)
                    {
                        threadYieldBurndown = 0;

                        Statistics.IncrementOutstandingDeliveries();
                        topMessage.State = CMqMessageState.OutForDelivery;

                        DistributeToSubscribers(topMessage).ContinueWith((t) =>
                        {
                            Statistics.DecrementOutstandingDeliveries();
                        });
                    }
                    else if (threadYieldBurndown < CMqDefaults.QUEUE_THREAD_DELIVERY_BURNDOWN)
                    {
                        threadYieldBurndown++;
                    }

                    var messagesWithDispositions = EnqueuedMessages.Read(m =>
                    {
                        return m.MessageBuffer.Where(o => o.State == CMqMessageState.DeadLetter || o.State == CMqMessageState.Drop).ToList();
                    });

                    if (messagesWithDispositions?.Count > 0)
                    {
                        EnqueuedMessages.TryWrite(m =>
                        {
                            foreach (var message in messagesWithDispositions)
                            {
                                switch (message.State)
                                {
                                    case CMqMessageState.DeadLetter:
                                        {
                                            if (Configuration.DeadLetterConfiguration != null)
                                            {
                                                _queueServer.ShovelToDeadLetter(Configuration.QueueName, message);
                                            }

                                            //Remove the message from the queue and cache.
                                            m.RemoveFromBufferAndDatabase(message);
                                            Statistics.DecrementQueueDepth();
                                        }
                                        break;
                                    case CMqMessageState.Drop:
                                        m.RemoveFromBufferAndDatabase(message);
                                        Statistics.DecrementQueueDepth();
                                        break;
                                }
                            }
                        });
                    }

                    #region Delivery Throttle.

                    if (Configuration.DeliveryThrottle > TimeSpan.Zero)
                    {
                        if (Configuration.DeliveryThrottle.TotalSeconds >= 1)
                        {
                            int sleepSeconds = (int)Configuration.DeliveryThrottle.TotalSeconds;
                            for (int sleep = 0; sleep < sleepSeconds && KeepRunning; sleep++)
                            {
                                Thread.Sleep(1000);
                            }
                        }
                        else
                        {
                            Thread.Sleep((int)Configuration.DeliveryThrottle.TotalMilliseconds);
                        }
                    }

                    #endregion
                }
                catch (Exception ex)
                {
                    _queueServer.InvokeOnLog(ex.GetBaseException());
                }

                if (threadYieldBurndown >= CMqDefaults.QUEUE_THREAD_DELIVERY_BURNDOWN && KeepRunning)
                {
                    threadYieldBurndown = CMqDefaults.QUEUE_THREAD_DELIVERY_BURNDOWN;
                    DeliveryThreadWaitEvent.WaitOne(10);
                }
            }
        }

        private async Task DistributeToSubscribers(EnqueuedMessage message)
        {
            try
            {
                message.State = await DistributeToSubscribersWithResolution(message);
            }
            catch
            {
                _queueServer.InvokeOnLog(CMqErrorLevel.Fatal, $"Failure of DistributeToSubscribersWithResolution [{Configuration.QueueName}].");
                message.State = CMqMessageState.Ready;
            }
        }

        /// <summary>
        /// Delivers the message to any remaining subscribers resulting in a defined state for the message
        /// </summary>
        private async Task<CMqMessageState> DistributeToSubscribersWithResolution(EnqueuedMessage message)
        {
            var subscriberDispositions = new SubscriberDispositions(this, message);

            if (subscriberDispositions.Remaining.Count == 0)
            {
                //The flow control below should not allow us to ever get here, but logically I feel we need to test for it.
                //Given that this is an "exception" to the proper flow, I think the only appropriate action to to give a
                //warning and dead-letter the message, unless at least one subscriber has been recorded as consuming the message.

                if (subscriberDispositions.ConsumedIDs.Count > 0)
                {
                    message.State = CMqMessageState.Drop;
                }
                return CMqMessageState.DeadLetter;
            }

            foreach (var subscriber in subscriberDispositions.Remaining)
            {
                if (KeepRunning == false)
                {
                    return CMqMessageState.Shutdown;
                }

                //Keep track of per-message-subscriber delivery metrics.
                if (message.SubscriberMessageDeliveries.TryGetValue(subscriber.SubscriberId, out var subscriberDeliveryStatistics))
                {
                    subscriberDeliveryStatistics.DeliveryAttemptCount++;
                }
                else
                {
                    subscriberDeliveryStatistics = new SubscriberMessageDeliveryStatistics() { DeliveryAttemptCount = 1 };
                    message.SubscriberMessageDeliveries.Add(subscriber.SubscriberId, subscriberDeliveryStatistics);
                }

                try
                {
                    subscriber.IncrementAttemptedDeliveryCount();

                    var deliveryResult = await _queueServer.DeliverMessage(subscriber.SubscriberId, Configuration.QueueName, message);

                    Statistics.IncrementDeliveredMessageCount();
                    subscriber.IncrementSuccessfulDeliveryCount();

                    if (deliveryResult.Disposition == CMqConsumptionDisposition.Consumed)
                    {
                        subscriber.IncrementConsumedDeliveryCount();

                        //The message was marked as consumed by the subscriber, so this subscriber is satisfied.
                        message.SatisfiedDeliverySubscriberIDs.Add(subscriber.SubscriberId);
                        message.ConsumedDeliverySubscriberIDs.Add(subscriber.SubscriberId);

                        if (Configuration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                        {
                            //Message was delivered and consumed. Given the queue consumption scheme, we just
                            //  need to break the delivery loop so the message can be removed from the queue.
                            return CMqMessageState.Drop;
                        }
                    }
                    else if (deliveryResult.Disposition == CMqConsumptionDisposition.NotConsumed)
                    {
                        //The message was marked as not-consumed by the subscriber, so this subscriber is satisfied.
                        message.SatisfiedDeliverySubscriberIDs.Add(subscriber.SubscriberId);
                    }
                    else if (deliveryResult.Disposition == CMqConsumptionDisposition.Defer)
                    {
                        //The message was marked as deferred by the subscriber, we will retry this subscriber at a later time.
                        //For all intents and purposes, we treat this subscriber as "not-yet-delivered".
                        message.DeferredUntil = DateTime.UtcNow + deliveryResult.DeferDuration;
                        message.DeferDuration = deliveryResult.DeferDuration;
                        message.DeferredCount++;
                        subscriber.IncrementDeferredDeliveryCount();
                        Statistics.IncrementDeferredDeliveryCount();

                        EnqueuedMessages.Read(m => m.Database.Store(message));
                    }
                    else if (deliveryResult.Disposition == CMqConsumptionDisposition.DeadLetter)
                    {
                        //When a subscriber responds with "DeadLetter" or "Drop", we short-circuit
                        //  the delivery flow logic and take the requested action on the message.
                        Statistics.IncrementExplicitDeadLetterCount();
                        return CMqMessageState.DeadLetter;
                    }
                    else if (deliveryResult.Disposition == CMqConsumptionDisposition.Drop)
                    {
                        //When a subscriber responds with "DeadLetter" or "Drop", we short-circuit
                        //  the delivery flow logic and take the requested action on the message.
                        Statistics.IncrementExplicitDropCount();
                        return CMqMessageState.Drop;
                    }
                }
                catch (Exception ex) //Delivery failure.
                {
                    Statistics.IncrementFailedDeliveryCount();
                    subscriber.IncrementFailedDeliveryCount();
                    _queueServer.InvokeOnLog(ex.GetBaseException());
                }

                //If we have tried to deliver this message to this subscriber too many times, then mark this subscriber-message as satisfied.
                if (Configuration.MaxDeliveryAttempts >= 0 && subscriberDeliveryStatistics.DeliveryAttemptCount >= Configuration.MaxDeliveryAttempts)
                {
                    //Even if we reached the max delivery count, there is not need to mark this as a
                    // failure if the subscriber is satisfied, as this would indicate that the latest
                    //  delivery attempt was finally successful.
                    if (message.SatisfiedDeliverySubscriberIDs.Contains(subscriber.SubscriberId) == false)
                    {
                        message.FailedDeliverySubscriberIDs.Add(subscriber.SubscriberId);
                    }
                }
            }

            if (KeepRunning == false)
            {
                return CMqMessageState.Shutdown;
            }

            //Get fresh subscriber dispositions.
            subscriberDispositions = new SubscriberDispositions(this, message);

            if (subscriberDispositions.Remaining.Count == 0)
            {
                //All subscribers have received a copy of the message or have received their maximum number
                //  of retries, so we can now remove the message from the queue.

                //If there were any failed deliveries, then we want to retain a copy of the message in the dead-letter queue.
                //This is because we handle FirstConsumedSubscriber consumption schemes in the logic above, meaning that this
                //  message was intended for successful delivery to all subscribers (which was obviously not achieved).
                if (message.FailedDeliverySubscriberIDs.Count != 0 && Configuration.DeadLetterConfiguration != null)
                {
                    return CMqMessageState.DeadLetter;
                }

                //Message was successfully delivered to all subscribers.
                return CMqMessageState.Drop;
            }
            else
            {
                //There are remaining un-satisfied subscribers. Mark the message as ready-for-delivery.
                return CMqMessageState.Ready;
            }
        }

        /// <summary>
        /// Load additional messages into the message buffer from the database.
        /// </summary>
        private bool HydrateMessageBuffer(int countToGet)
        {
            if (Configuration.PersistenceScheme != CMqPersistenceScheme.Persistent)
            {
                return false;
            }

            _queueServer.InvokeOnLog(CMqErrorLevel.Verbose, $"Re-hydrating message buffer for [{Configuration.QueueName}].");

            return EnqueuedMessages.TryWrite(m =>
            {
                if (m.Database == null)
                {
                    throw new Exception($"Persistence database has not been initialized for [{Configuration.QueueName}].");
                }

                ulong? maxBufferedSerialNumber = m.MessageBuffer.Count > 0 ? m.MessageBuffer.Max(o => o.SerialNumber) : null;
                int messagesLoaded = 0;

                using var iterator = m.Database.NewIterator();
                for (iterator.SeekToFirst(); iterator.Valid() && messagesLoaded < countToGet; iterator.Next())
                {
                    var persistedSerialNumberBytes = iterator.Key();
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(persistedSerialNumberBytes); // Convert key back to little-endian.
                    }
                    var persistedSerialNumber = BitConverter.ToUInt64(persistedSerialNumberBytes);

                    if (maxBufferedSerialNumber == null || persistedSerialNumber > maxBufferedSerialNumber)
                    {
                        var json = Encoding.UTF8.GetString(iterator.Value());
                        var persistedMessage = JsonSerializer.Deserialize<EnqueuedMessage>(json);
                        if (persistedMessage != null)
                        {
                            m.MessageBuffer.Add(persistedMessage);
                            messagesLoaded++;
                        }
                    }
                }
            });
        }

        public void InitializePersistentDatabase()
        {
            if (Configuration.PersistenceScheme != CMqPersistenceScheme.Persistent)
            {
                return;
            }

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Creating persistent path for [{Configuration.QueueName}].");

            var databasePath = Path.Join(_queueServer.Configuration.PersistencePath, "messages", Configuration.QueueName);
            Directory.CreateDirectory(databasePath);

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Instantiating persistent database for [{Configuration.QueueName}].");
            var options = new DbOptions().SetCreateIfMissing(true);
            var persistenceDatabase = RocksDb.Open(options, databasePath);

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Loading persistent messages for [{Configuration.QueueName}].");

            EnqueuedMessages.Write(m =>
            {
                Statistics.SetQueueDepth(0);

                int messagesLoaded = 0;
                var lastSerialNumberBytes = BitConverter.GetBytes((ulong)0);

                using var iterator = persistenceDatabase.NewIterator();
                for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                {
                    lastSerialNumberBytes = iterator.Key();
                    messagesLoaded++;
                }

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lastSerialNumberBytes); // Convert back to little-endian.
                }
                var lastSerialNumber = BitConverter.ToUInt64(lastSerialNumberBytes);

                _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Loaded {messagesLoaded:n0} messages for [{Configuration.QueueName}] with serial number 0x{lastSerialNumber:x}.");

                Statistics.SetQueueDepth(messagesLoaded);
                Statistics.SetLastSerialNumber(lastSerialNumber);

                m.Database = persistenceDatabase;
            });
        }

        public void StartAsync()
        {
            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Starting delivery thread for [{Configuration.QueueName}].");
            KeepRunning = true;
            _deliveryThread.Start();
        }

        public void StopAsync()
        {
            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Signaling shutdown for [{Configuration.QueueName}].");
            KeepRunning = false;
        }

        public void WaitOnStop()
        {
            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Waiting on delivery thread to quit for [{Configuration.QueueName}].");
            _deliveryThread.Join();

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Shutting down database connection for [{Configuration.QueueName}].");
            EnqueuedMessages.Write(m =>
            {
                if (m.Database != null)
                {
                    m.Database?.Dispose();
                    m.Database = null;
                }
            });
        }
    }
}
