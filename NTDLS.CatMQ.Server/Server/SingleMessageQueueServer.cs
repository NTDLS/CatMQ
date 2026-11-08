using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;
using NTDLS.Semaphore;
using RocksDbSharp;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A named message queue and its delivery thread.
    /// </summary>
    internal class SingleMessageQueueServer
    {
        private readonly CMqServer _queueServer;
        private Thread? _deliveryThread;
        private volatile bool _isRunning;
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// The highest serial number that has been loaded from the database into the message buffer.
        /// Only accessed while holding the write lock on EnqueuedMessages.
        /// </summary>
        private ulong? _lastHydratedSerialNumber;

        private int _consecutiveDeliveryFailures;
        private long _deliveryBackoffUntilTicks;

        public bool IsInitialized { get; private set; }
        public bool IsStarted => _isRunning;

        /// <summary>
        /// This property is set when there is an error that prevents the queue from functioning properly, such as a failure to initialize the persistent database.
        /// When this property is set, the queue will not accept messages for delivery to subscribers and will not attempt to deliver messages to subscribers.
        /// The presence of an error message in this property should be considered a critical failure of the queue.
        /// </summary>
        public string? ErrorMessage { get; private set; }

        internal AutoResetEvent DeliveryThreadWaitEvent = new(false);

        /// <summary>
        /// List of subscriber connection IDs.
        /// We use OptimisticCriticalResource instead of ConcurrentDictionary to avoid
        ///     lock interleaving thereby eliminating the possibility of deadlocks.
        /// </summary>
        internal OptimisticCriticalResource<Dictionary<Guid, CMqSubscriberDescriptor>> Subscribers { get; set; } = new();

        /// <summary>
        /// Messages that are enqueued in this list.
        /// </summary>
        internal OptimisticCriticalResource<EnqueuedMessageContainer> EnqueuedMessages { get; set; } = new();

        internal CMqQueueConfiguration Configuration { get; set; }

        internal MessageQueueStatistics Statistics { get; set; } = new();

        internal HistoricalStatistics HistoricalStatistics { get; private set; }

        /// <summary>
        /// The consumers that receive messages by asking for them, by consumer name.
        /// </summary>
        internal ConcurrentDictionary<string, LeaseDeliveryTransport> LeaseConsumers { get; } = new();

        /// <summary>
        /// Makes checking the depth limit and storing a message one step, for queues that have a limit.
        /// </summary>
        internal object DepthLimitLock { get; } = new();

        /// <summary>
        /// Signaled when the queue is shutting down or being deleted.
        /// </summary>
        internal CancellationToken ShutdownToken => _cts.Token;

        public SingleMessageQueueServer(CMqServer mqServer, CMqQueueConfiguration queueConfiguration)
        {
            _queueServer = mqServer;
            Configuration = queueConfiguration;
            HistoricalStatistics = new HistoricalStatistics(_queueServer.Configuration);
        }

        /// <summary>
        /// Replaces the configuration of the running queue. The delivery thread picks up the new values on its next cycle.
        /// </summary>
        internal void ApplyConfiguration(CMqQueueConfiguration queueConfiguration)
        {
            Configuration = queueConfiguration;
            DeliveryThreadWaitEvent.Set();
        }

        private void DeliveryThreadProc()
        {
            var lastCheckpoint = DateTime.UtcNow;
            bool attemptBufferRehydration = false;

            int threadYieldBurndown = CMqDefaults.QUEUE_THREAD_DELIVERY_BURNDOWN; //Just used to omit waiting. We want to spin fast when we are delivering messages.

            int consecutiveExceptions = 0;

            while (_isRunning)
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

                                var ticketPersistenceCheckpoint = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.PersistenceCheckpoint);
                                o.Database.Flush(new FlushOptions());
                                ticketPersistenceCheckpoint?.Accumulate();
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

                        var ticketMessageBufferHydration = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.MessageBufferHydration);
                        HydrateMessageBuffer(CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_BUFFER_SIZE);
                        ticketMessageBufferHydration?.Accumulate();
                    }

                    var ticketDeliveryMessageLock = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.DeliveryMessageLock);
                    if (EnqueuedMessages.TryReadAll([Subscribers], CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                    {
                        ticketDeliveryMessageLock?.Accumulate();

                        if (Configuration.MaxMessageAge > TimeSpan.Zero) //NULL and zero both mean that messages never expire.
                        {
                            var ticketExpirationCheck = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.ExpirationCheck);

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
                                    if (Configuration.DeadLetterConfiguration.MaxMessageAge > TimeSpan.Zero
                                        && (now - message.Timestamp) > Configuration.DeadLetterConfiguration.MaxMessageAge)
                                    {
                                        //Even too old for the dead-letter queue, discard expired message.
                                        message.State = CMqMessageState.Drop;
                                    }
                                    else
                                    {
                                        message.State = CMqMessageState.DeadLetter;
                                    }
                                }
                                else
                                {
                                    //No dead-letter queue, just drop the message.
                                    message.State = CMqMessageState.Drop;
                                }

                                Statistics.IncrementExpiredMessageCount();
                            }

                            ticketExpirationCheck?.Accumulate();
                        }

                        if (Statistics.OutstandingDeliveries < Configuration.MaxOutstandingDeliveries
                            && Environment.TickCount64 >= Interlocked.Read(ref _deliveryBackoffUntilTicks))
                        {
                            var ticketDeliverySubscribersLock = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.DeliverySubscribersLock);

                            Subscribers.Read(s => //This lock is already held.
                            {
                                ticketDeliverySubscribersLock?.Accumulate();

                                //We only process a queue if it has subscribers that can accept a delivery, so that
                                //  we do not discard messages as delivered for queues with no subscribers.
                                var readyConsumerIDs = s.Values.Where(o => o.Transport.IsReady).Select(o => o.ConsumerId).ToHashSet();
                                if (readyConsumerIDs.Count > 0)
                                {
                                    var consumerIDs = s.Values.Select(o => o.ConsumerId).ToHashSet();

                                    static bool NeedsDelivery(EnqueuedMessage message, HashSet<Guid> ids)
                                        => ids.Any(id => message.SatisfiedDeliverySubscriberIDs.Contains(id) == false
                                            && message.DeliveryLimitReachedSubscriberIDs.Contains(id) == false);

                                    //Get the first message that one of the ready consumers still needs. A message that is only
                                    //  waiting on a consumer that is not ready must not hold up the ones behind it. A message
                                    //  that no consumer needs is also selected, because delivery is what finishes it off.
                                    topMessage = m.MessageBuffer.FirstOrDefault(o =>
                                        o.State == CMqMessageState.Ready && (o.DeferredUntil == null || now >= o.DeferredUntil)
                                        && (NeedsDelivery(o, readyConsumerIDs) || NeedsDelivery(o, consumerIDs) == false));

                                    if (Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                                    {
                                        //We rehydrate the queue from the database when we have a queue-depth and either we didn't
                                        //  get a top-message or we are under the minimum buffer size. We have to check for the NULL
                                        //  top-message because it could be that we do not have any qualified messages in the buffer
                                        //  (because all of them are deferred) even though we do have messages in the buffer.
                                        if (Statistics.QueueDepth > m.MessageBuffer.Count
                                            && m.MessageBuffer.Count < CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_MAX_BUFFER
                                            && (topMessage == null || m.MessageBuffer.Count < CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_MIN_BUFFER))
                                        {
                                            //If we have more items in the queue than we have in the buffer, then trigger a rehydrate.
                                            attemptBufferRehydration = (Statistics.QueueDepth > m.MessageBuffer.Count);
                                        }
                                    }
                                }
                            });
                        }
                    }) == false)
                    {
                        ticketDeliveryMessageLock?.Accumulate();
                    }

                    if (topMessage != null)
                    {
                        threadYieldBurndown = 0;

                        Statistics.IncrementOutstandingDeliveries();
                        topMessage.State = CMqMessageState.OutForDelivery;

                        if (Configuration.AsynchronousDelivery)
                        {
                            _ = DistributeToSubscribersAsync(topMessage);
                        }
                        else
                        {
                            DistributeToSubscribersAsync(topMessage).GetAwaiter().GetResult();
                        }
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
                        //Wait briefly: a single attempt can lose to busy readers forever, leaving finished messages in the buffer.
                        EnqueuedMessages.TryWrite(CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                        {
                            foreach (var message in messagesWithDispositions)
                            {
                                //The message may have been purged while it was out for delivery. If so, there is nothing left to dispose of.
                                if (m.MessageBuffer.Contains(message) == false)
                                {
                                    continue;
                                }

                                switch (message.State)
                                {
                                    case CMqMessageState.DeadLetter:
                                        {
                                            if (Configuration.DeadLetterConfiguration != null)
                                            {
                                                var ticketDeadLetter = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.DeadLetter);
                                                _queueServer.MoveMessageToDeadLetter(Configuration.QueueName, message);
                                                ticketDeadLetter?.Accumulate();
                                            }

                                            //Remove the message from the queue and cache.
                                            m.RemoveFromBufferAndDatabase(_queueServer, message);
                                            Statistics.DecrementQueueDepth();
                                        }
                                        break;
                                    case CMqMessageState.Drop:
                                        m.RemoveFromBufferAndDatabase(_queueServer, message);
                                        Statistics.DecrementQueueDepth();
                                        break;
                                }
                            }
                        });
                    }

                    #region Delivery Throttle.

                    if (Configuration.DeliveryThrottle > TimeSpan.Zero)
                    {
                        _cts.Token.WaitHandle.WaitOne((int)Configuration.DeliveryThrottle.TotalMilliseconds);
                    }

                    #endregion
                    consecutiveExceptions = 0;
                }
                catch (OperationCanceledException) when (!_isRunning)
                {
                    //Normal during shutdown or queue deletion.
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveExceptions++;
                    _queueServer.InvokeOnLog(ex.GetBaseException());

                    _cts.Token.WaitHandle.WaitOne(100);

                    if (consecutiveExceptions > 1000)
                    {
                        _queueServer.InvokeOnLog(CMqErrorLevel.Fatal, $"Delivery thread for [{Configuration.QueueName}] has encountered [{consecutiveExceptions}] consecutive exceptions. Signaling shutdown.");
                        _isRunning = false;
                        _cts.Cancel();
                    }
                }

                if (threadYieldBurndown >= CMqDefaults.QUEUE_THREAD_DELIVERY_BURNDOWN && _isRunning)
                {
                    threadYieldBurndown = CMqDefaults.QUEUE_THREAD_DELIVERY_BURNDOWN;

                    var ticketDeliveryYield = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.DeliveryYield);
                    DeliveryThreadWaitEvent.WaitOne(10);
                    ticketDeliveryYield?.Accumulate();
                }
            }
        }

        private async Task DistributeToSubscribersAsync(EnqueuedMessage message)
        {
            try
            {
                message.State = await DistributeToSubscribersWithDisposition(message);
            }
            catch (Exception ex)
            {
                message.State = CMqMessageState.Ready;
                _queueServer.InvokeOnLog(CMqErrorLevel.Fatal, $"Failure of DistributeToSubscribersWithResolution [{Configuration.QueueName}].");
                _queueServer.InvokeOnLog(ex.GetBaseException());
            }
            finally
            {
                Statistics.DecrementOutstandingDeliveries();

                //A delivery slot just opened up. The delivery thread may be idling because all of the slots
                //  were taken, so wake it now rather than leaving the subscriber idle until the wait times out.
                DeliveryThreadWaitEvent.Set();
            }
        }

        /// <summary>
        /// Failed messages go straight back to the top of the queue, so without a pause a failing subscriber
        /// is retried as fast as the server can spin. Each consecutive failure doubles the pause, up to a limit.
        /// </summary>
        private void ExtendDeliveryBackoff()
        {
            int failures = Interlocked.Increment(ref _consecutiveDeliveryFailures);
            long backoffMs = Math.Min(1L << Math.Min(failures, 30), CMqDefaults.MAX_DELIVERY_FAILURE_BACKOFF_MS);
            Interlocked.Exchange(ref _deliveryBackoffUntilTicks, Environment.TickCount64 + backoffMs);
        }

        private void ResetDeliveryBackoff()
        {
            Interlocked.Exchange(ref _consecutiveDeliveryFailures, 0);
            Interlocked.Exchange(ref _deliveryBackoffUntilTicks, 0);
        }

        /// <summary>
        /// Creates the transport-neutral form of a message, as it stands for this delivery attempt.
        /// </summary>
        private CMqReceivedMessage CreateDelivery(EnqueuedMessage message, int deliveryAttempt)
        {
            return new CMqReceivedMessage(Configuration.QueueName, message.SerialNumber, message.MessageType, message.Body)
            {
                MessageId = message.MessageId,
                ContentType = message.ContentType,
                Headers = message.Headers,
                BodyIsBase64 = message.BodyIsBase64,
                DeliveryAttempt = deliveryAttempt,
                Timestamp = message.Timestamp,
                DeferDuration = message.DeferDuration,
                DeferredCount = message.DeferredCount,
                SubscriberDeliveryCount = message.SubscriberMessageDeliveries.Count,
                SatisfiedSubscriberCount = message.SatisfiedDeliverySubscriberIDs.Count,
                FailedSubscriberCount = message.DeliveryLimitReachedSubscriberIDs.Count
            };
        }

        /// <summary>
        /// Delivers the message to any remaining subscribers and waits for a disposition from the subscriber.
        /// </summary>
        private async Task<CMqMessageState> DistributeToSubscribersWithDisposition(EnqueuedMessage message)
        {
            var subscriberDispositions = new SubscriberDispositions(this, message);

            if (subscriberDispositions.Remaining.Count == 0)
            {
                // The flow control below should not allow us to ever get here, but logically I feel we need to test for it.
                // Given that this is an "exception" to the proper flow, I think the only appropriate action to to give a
                //  warning and dead-letter the message, unless at least one subscriber has been recorded as consuming the message.

                if (subscriberDispositions.ConsumedSubscriberIDs.Count > 0)
                {
                    //Someone received, the message - so lets just drop it.
                    return CMqMessageState.Drop;
                }
                return CMqMessageState.DeadLetter;
            }

            if (subscriberDispositions.Deliverable.Count == 0)
            {
                //The consumers that were ready when this message was selected no longer are. Leave it for later.
                return CMqMessageState.Ready;
            }

            var failureToDeliverSubscriberIDs = new HashSet<Guid>();

            foreach (var subscriber in subscriberDispositions.Deliverable)
            {
                if (_isRunning == false)
                {
                    return CMqMessageState.Shutdown;
                }

                //Keep track of per-message-subscriber delivery metrics.
                if (message.SubscriberMessageDeliveries.TryGetValue(subscriber.ConsumerId, out var subscriberDeliveryStatistics))
                {
                    subscriberDeliveryStatistics.DeliveryAttemptCount++;
                }
                else
                {
                    subscriberDeliveryStatistics = new SubscriberMessageDeliveryStatistics() { DeliveryAttemptCount = 1 };
                    message.SubscriberMessageDeliveries.Add(subscriber.ConsumerId, subscriberDeliveryStatistics);
                }

                try
                {
                    subscriber.IncrementAttemptedDeliveryCount();

                    var deliveryResult = await subscriber.Transport.DeliverAsync(CreateDelivery(message, subscriberDeliveryStatistics.DeliveryAttemptCount), _cts.Token);

                    ResetDeliveryBackoff();
                    Statistics.IncrementDeliveredMessageCount();
                    subscriber.IncrementSuccessfulDeliveryCount();

                    if (deliveryResult.Disposition == CMqConsumptionDisposition.Consumed)
                    {
                        subscriber.IncrementConsumedDeliveryCount();

                        //The message was marked as consumed by the subscriber, so this subscriber is satisfied.
                        message.SatisfiedDeliverySubscriberIDs.Add(subscriber.ConsumerId);
                        message.ConsumedDeliverySubscriberIDs.Add(subscriber.ConsumerId);

                        if (Configuration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                        {
                            //Message was delivered and consumed. Given the queue consumption scheme, we just
                            //  need to break the delivery loop so the message can be removed from the queue.
                            return CMqMessageState.Drop;
                        }
                        else if (Configuration.ConsumptionScheme == CMqConsumptionScheme.AllSubscribersSatisfied)
                        {
                            //Message was delivered and consumed, but we still need to deliver it to the remaining subscribers.
                            //We do not break the loop here, as we want to ensure that all subscribers receive a copy of the message.
                        }
                        else
                        {
                            //This is an unexpected consumption scheme, we should not be here.
                            _queueServer.InvokeOnLog(CMqErrorLevel.Fatal, $"Unexpected consumption scheme [{Configuration.ConsumptionScheme}] for queue [{Configuration.QueueName}].");
                            return CMqMessageState.DeadLetter;
                        }
                    }
                    else if (deliveryResult.Disposition == CMqConsumptionDisposition.NotInterested)
                    {
                        //The message was marked as not-interested by the subscriber, so this subscriber is satisfied.
                        message.SatisfiedDeliverySubscriberIDs.Add(subscriber.ConsumerId);
                    }
                    else if (deliveryResult.Disposition == CMqConsumptionDisposition.NotConsumed)
                    {
                        //The message was marked as not-consumed by the subscriber, so this subscriber is NOT satisfied.
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

                        var ticketDeferReadLock = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.DeferReadLock);
                        //The rocksdb database can handle concurrent writes, so we really only need to obtain the object - not lock it.
                        EnqueuedMessages.Read(messages =>
                        {
                            ticketDeferReadLock?.Accumulate();

                            //If the queue was purged while the message was out for delivery, do not write it back to the database.
                            if (messages.MessageBuffer.Contains(message))
                            {
                                var ticketDeferStore = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.DeferStore);
                                messages.Database.Store(message);
                                ticketDeferStore?.Accumulate();
                            }
                        });

                        //We return "Ready" so that the message can be re-delivered later and we do not deliver it to any other subscribers.
                        return CMqMessageState.Ready;
                    }
                    else if (deliveryResult.Disposition == CMqConsumptionDisposition.DeadLetter)
                    {
                        //When a subscriber responds with "DeadLetter" we short-circuit
                        //  the delivery flow logic and take the requested action on the message.
                        Statistics.IncrementExplicitDeadLetterCount();
                        return CMqMessageState.DeadLetter;
                    }
                    else if (deliveryResult.Disposition == CMqConsumptionDisposition.Drop)
                    {
                        //When a subscriber responds with "Drop", we short-circuit
                        //  the delivery flow logic and take the requested action on the message.
                        Statistics.IncrementExplicitDropCount();
                        return CMqMessageState.Drop;
                    }
                    else
                    {
                        throw new Exception($"Unexpected delivery disposition [{deliveryResult.Disposition}] for queue [{Configuration.QueueName}].");
                    }
                }
                catch (CMqDeliveryNotAcceptedException)
                {
                    //The consumer never had this message, so it is left in the queue with nothing held against it.
                    subscriberDeliveryStatistics.DeliveryAttemptCount--;
                    failureToDeliverSubscriberIDs.Add(subscriber.ConsumerId);
                    continue;
                }
                catch (Exception ex) //Delivery failure.
                {
                    failureToDeliverSubscriberIDs.Add(subscriber.ConsumerId);
                    ExtendDeliveryBackoff();
                    Statistics.IncrementFailedDeliveryCount();
                    subscriber.IncrementFailedDeliveryCount();
                    _queueServer.InvokeOnLog(ex.GetBaseException());
                }

                //If we have tried to deliver this message to this subscriber too many times, then mark this subscriber-message as satisfied.
                if (Configuration.MaxDeliveryAttempts > 0 && subscriberDeliveryStatistics.DeliveryAttemptCount >= Configuration.MaxDeliveryAttempts)
                {
                    //Even if we reached the max delivery count, there is no need to mark this as a failure if the
                    //  subscriber is satisfied (as that indicates that the latest delivery attempt was finally successful).
                    if (message.SatisfiedDeliverySubscriberIDs.Contains(subscriber.ConsumerId) == false)
                    {
                        message.DeliveryLimitReachedSubscriberIDs.Add(subscriber.ConsumerId);
                    }
                }
            }

            if (_isRunning == false)
            {
                return CMqMessageState.Shutdown;
            }

            bool allDeliveriesFailed = !subscriberDispositions.Deliverable.Select(o => o.ConsumerId).Except(failureToDeliverSubscriberIDs).Any();

            //Get fresh subscriber dispositions.
            subscriberDispositions = new SubscriberDispositions(this, message);

            if (allDeliveriesFailed)
            {
                //All of the deliveries failed. They may even have disconnected mid-delivery and the subscriber count may now be zero.
                //Keep the message in the queue for re-delivery later or eventual expiration.
                return CMqMessageState.Ready;
            }
            else if (subscriberDispositions.Remaining.Count == 0)
            {
                //All subscribers have received a copy of the message or have received their maximum number
                //  of retries, so we can now remove the message from the queue.

                //If there were any failed deliveries, then we want to retain a copy of the message in the dead-letter queue.
                //This is because we handle FirstConsumedSubscriber consumption schemes in the logic above, meaning that this
                //  message was intended for successful delivery to all subscribers (which was obviously not achieved).
                if (message.DeliveryLimitReachedSubscriberIDs.Count != 0 && Configuration.DeadLetterConfiguration != null)
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

            var ticketMessageBufferHydrationWriteLock = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.MessageBufferHydrationWriteLock);
            bool result = EnqueuedMessages.TryWrite(CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
            {
                ticketMessageBufferHydrationWriteLock?.Accumulate();

                if (m.Database == null)
                {
                    throw new Exception($"Persistence database has not been initialized for [{Configuration.QueueName}].");
                }

                ulong? maxBufferedSerialNumber = m.MessageBuffer.Count > 0 ? m.MessageBuffer.Max(o => o.SerialNumber) : null;

                //Resume after the newest message we have ever loaded. Seeking from the start of the database means walking
                //  the tombstone of every message consumed since the last compaction, which takes hundreds of milliseconds
                //  on a busy queue, all while holding the write lock.
                ulong? resumeAfterSerialNumber = maxBufferedSerialNumber;
                if (_lastHydratedSerialNumber != null && (resumeAfterSerialNumber == null || _lastHydratedSerialNumber > resumeAfterSerialNumber))
                {
                    resumeAfterSerialNumber = _lastHydratedSerialNumber;
                }

                int messagesLoaded = LoadMessagesFromDatabase(m, resumeAfterSerialNumber, countToGet);

                if (messagesLoaded == 0 && resumeAfterSerialNumber != null && m.MessageBuffer.Count == 0)
                {
                    //The queue has depth but there is nothing past the resume point. Serial numbers are assigned before messages
                    //  are stored, so a message can land behind the resume point. Scan from the start so that it is not stranded.
                    LoadMessagesFromDatabase(m, null, countToGet);
                }
            });

            if (result == false)
            {
                ticketMessageBufferHydrationWriteLock?.Accumulate();
            }

            return result;
        }

        /// <summary>
        /// Adds messages from the database to the message buffer, starting after the given serial number (or from the start
        /// of the database when NULL). The caller must hold the write lock on the container.
        /// </summary>
        private int LoadMessagesFromDatabase(EnqueuedMessageContainer m, ulong? afterSerialNumber, int countToGet)
        {
            int messagesLoaded = 0;

            using var iterator = m.Database!.NewIterator();
            if (afterSerialNumber != null)
            {
                iterator.Seek((afterSerialNumber.Value + 1).ToKey());
            }
            else
            {
                iterator.SeekToFirst();
            }

            for (; iterator.Valid() && messagesLoaded < countToGet; iterator.Next())
            {
                var persistedSerialNumberBytes = iterator.Key();
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(persistedSerialNumberBytes); // Convert key back to little-endian.
                }
                var persistedSerialNumber = BitConverter.ToUInt64(persistedSerialNumberBytes);

                if (afterSerialNumber == null || persistedSerialNumber > afterSerialNumber)
                {
                    var json = Encoding.UTF8.GetString(iterator.Value());
                    var persistedMessage = JsonSerializer.Deserialize<EnqueuedMessage>(json);
                    if (persistedMessage != null)
                    {
                        if (persistedMessage.MessageId == Guid.Empty)
                        {
                            //Persisted by a version that did not assign message IDs. Give it one and write it
                            //  back so that it keeps the same ID if it is redelivered after another restart.
                            persistedMessage.MessageId = Guid.NewGuid();
                            m.Database.Store(persistedMessage);
                        }

                        m.MessageBuffer.Add(persistedMessage);
                        messagesLoaded++;

                        if (_lastHydratedSerialNumber == null || persistedSerialNumber > _lastHydratedSerialNumber)
                        {
                            _lastHydratedSerialNumber = persistedSerialNumber;
                        }
                    }
                }
            }

            return messagesLoaded;
        }

        public void InitializePersistentDatabase()
        {
            var ticketPersistentInitialization = _queueServer.Instrumentation?.CreateTicket(CMqInstrumentationEventType.PersistentInitialization);

            try
            {
                if (Configuration.PersistenceScheme != CMqPersistenceScheme.Persistent)
                {
                    ticketPersistentInitialization?.Accumulate();
                    IsInitialized = true;
                    return;
                }

                _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Creating persistent path for [{Configuration.QueueName}].");

                var databasePath = Path.Join(_queueServer.Configuration.PersistencePath, "messages", Configuration.PathPart);
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

                IsInitialized = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Critical error occurred initializing persistence database for [{Configuration.QueueName}]. Exception: {ex.GetBaseException().Message}";
                _queueServer.InvokeOnLog(CMqErrorLevel.Error, ErrorMessage);
            }

            ticketPersistentInitialization?.Accumulate();
        }

        public void Start()
        {
            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Starting delivery thread for [{Configuration.QueueName}].");
            _isRunning = true;

            //This is a dedicated thread because it blocks while it idles. On the thread pool, one per queue can occupy every
            //  minimum thread on a small server, which starves the delivery replies, enqueues and web requests that need the pool.
            _deliveryThread = new Thread(() =>
            {
                try
                {
                    DeliveryThreadProc();
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Critical error occurred in delivery thread [{Configuration.QueueName}]. Exception: {ex.GetBaseException().Message}";
                    _queueServer.InvokeOnLog(CMqErrorLevel.Error, ErrorMessage);
                }
            })
            {
                IsBackground = true,
                Name = $"CatMQ.Delivery.{Configuration.QueueName}"
            };
            _deliveryThread.Start();
        }

        public void SignalShutdown()
        {
            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Signaling shutdown for [{Configuration.QueueName}].");
            _isRunning = false;
            _cts.Cancel();
        }

        public void WaitForShutdown()
        {
            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Waiting on delivery thread to quit for [{Configuration.QueueName}].");
            _deliveryThread?.Join();

            //Asynchronous deliveries are not tracked by the delivery task. Wait for them to finish so that they
            //  do not touch the database after it has been disposed.
            var deadline = Environment.TickCount64 + CMqDefaults.DEFAULT_LOCK_TIMEOUT_MS * 3;
            while (Statistics.OutstandingDeliveries > 0 && Environment.TickCount64 < deadline)
            {
                Thread.Sleep(10);
            }

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Shutting down database connection for [{Configuration.QueueName}].");
            EnqueuedMessages.Write(m =>
            {
                if (m.Database != null)
                {
                    m.Database?.Dispose();
                    m.Database = null;
                }
            });

            _cts.Dispose();
        }
    }
}
