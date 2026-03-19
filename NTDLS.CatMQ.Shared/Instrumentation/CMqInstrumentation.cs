using System.Collections.Concurrent;
using System.Diagnostics;

namespace NTDLS.CatMQ.Shared.Instrumentation
{
    /// <summary>
    /// Provides instrumentation support for tracking and accumulating event metrics within the message queue system.
    /// </summary>
    /// <remarks>This class enables the measurement of event durations and counts for various instrumentation
    /// event types. It is intended for use in performance monitoring and diagnostics scenarios. Instances of this class
    /// are thread-safe and can be used concurrently across multiple threads.</remarks>
    public class CMqInstrumentation
    {
        internal ConcurrentDictionary<CMqInstrumentationEventType, CMqInstrumentationEvent> Events { get; private set; } = new();

        /// <summary>
        /// Creates a new instrumentation ticket for the specified event type.
        /// </summary>
        /// <param name="eventType">The type of instrumentation event for which to create the ticket.</param>
        /// <returns>A new instance of CMqInstrumentationTicket associated with the specified event type.</returns>
        public CMqInstrumentationTicket CreateTicket(CMqInstrumentationEventType eventType)
        {
            return new CMqInstrumentationTicket(this, eventType);
        }

        /// <summary>
        /// Creates a thread-safe snapshot of the current instrumentation events.
        /// </summary>
        /// <remarks>Use this method to obtain a consistent view of the instrumentation events for
        /// analysis or reporting purposes. The snapshot is independent of the original collection and can be safely
        /// enumerated or modified without affecting the source.</remarks>
        /// <returns>A new ConcurrentDictionary containing the current set of instrumentation events and their associated types.
        /// The returned dictionary reflects the state of the events at the time of the call and is not affected by
        /// subsequent changes.</returns>
        public ConcurrentDictionary<CMqInstrumentationEventType, CMqInstrumentationEvent> Snapshot()
        {
            return new ConcurrentDictionary<CMqInstrumentationEventType, CMqInstrumentationEvent>(Events);
        }

        /// <summary>
        /// Records the completion of an event by updating its count and total elapsed time based on the provided
        /// instrumentation ticket.
        /// </summary>
        /// <remarks>If the event type specified in the ticket does not exist, a new event entry is
        /// created. This method is thread-safe for individual event types, but concurrent updates to different event
        /// types are not synchronized.</remarks>
        /// <param name="ticket">The instrumentation ticket containing the event type and start timestamp to be accumulated. Cannot be null.</param>
        public void Accumulate(CMqInstrumentationTicket ticket)
        {
            long end = Stopwatch.GetTimestamp();
            double elapsedMs = (end - ticket.Start) * 1000.0 / Stopwatch.Frequency;

            if (Events.TryGetValue(ticket.EventType, out var existingEvent))
            {
                lock (existingEvent.LockObject)
                {
                    existingEvent.Count++;
                    existingEvent.Milliseconds += elapsedMs;
                }
            }
            else
            {
                Events[ticket.EventType] = new CMqInstrumentationEvent()
                {
                    Count = 1,
                    Milliseconds = elapsedMs
                };
            }
        }
    }
}
