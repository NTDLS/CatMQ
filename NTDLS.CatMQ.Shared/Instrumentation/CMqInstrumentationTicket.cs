using System.Diagnostics;

namespace NTDLS.CatMQ.Shared.Instrumentation
{
    /// <summary>
    /// Represents a ticket used to track and accumulate instrumentation events for message queue operations.
    /// </summary>
    /// <remarks>A CMqInstrumentationTicket is typically created when an instrumentation event begins and is
    /// used to record timing or other metrics for that event. The ticket should be passed to the associated
    /// instrumentation system to accumulate results when the event completes.</remarks>
    public class CMqInstrumentationTicket
    {
        private readonly CMqInstrumentation _instrumentation;
        /// <summary>
        /// Gets the type of event that is being tracked by the instrumentation system.
        /// </summary>
        public CMqInstrumentationEventType EventType { get; private set; }
        /// <summary>
        /// Gets the timestamp indicating when the instrumentation event started.
        /// </summary>
        public long Start { get; private set; }

        /// <summary>
        /// Initializes a new instance of the CMqInstrumentationTicket class for tracking a specific instrumentation
        /// event.
        /// </summary>
        /// <param name="instrumentation">The instrumentation context used to record and manage event metrics. Cannot be null.</param>
        /// <param name="eventType">The type of instrumentation event to be tracked by this ticket.</param>
        public CMqInstrumentationTicket(CMqInstrumentation instrumentation, CMqInstrumentationEventType eventType)
        {
            _instrumentation = instrumentation;
            EventType = eventType;
            Start = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Accumulates the current state or metrics for this instance.
        /// </summary>
        /// <remarks>Call this method to record or aggregate the current values managed by this object.
        /// The specific behavior depends on the instrumentation implementation associated with this instance.</remarks>
        public void Accumulate()
            => _instrumentation.Accumulate(this);
    }
}
