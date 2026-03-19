namespace NTDLS.CatMQ.Shared.Instrumentation
{
    /// <summary>
    /// Represents instrumentation data for a message queue event, including timing and count metrics.
    /// </summary>
    public class CMqInstrumentationEvent
    {
        internal Lock LockObject { get; private set; } = new();

        /// <summary>
        /// Cumulative time in milliseconds for all occurrences of the associated instrumentation event type.
        /// </summary>
        public double Milliseconds { get; set; }

        /// <summary>
        /// Cumulative count of all occurrences of the associated instrumentation event type.
        /// </summary>
        public long Count { get; set; }
    }
}
