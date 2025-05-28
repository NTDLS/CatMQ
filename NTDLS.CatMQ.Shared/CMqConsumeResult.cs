namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// Result of subscriber receiving a message from a server queue.
    /// </summary>
    public class CMqConsumeResult
    {
        /// <summary>
        /// Denotes if and how the message is marked as delivered.
        /// </summary>
        public CMqConsumptionDisposition Disposition { get; set; }

        /// <summary>
        /// When the consumption disposition is deferred, this is the amount of time to delay the redelivery of the message.
        /// </summary>
        public TimeSpan? DeferDuration { get; set; }

        /// <summary>
        /// Represents the result of a message consumption operation, including its disposition.
        /// </summary>
        /// <param name="disposition">The disposition of the consumed message, indicating the outcome of the consumption process.</param>
        public CMqConsumeResult(CMqConsumptionDisposition disposition)
        {
            Disposition = disposition;
        }

        /// <summary>
        /// Shortcut function that returns a CMqConsumeResult with the Consumed disposition.
        /// </summary>
        public static CMqConsumeResult Consumed()
            => new(CMqConsumptionDisposition.Consumed);

        /// <summary>
        /// Shortcut function that returns a CMqConsumeResult with the NotConsumed disposition.
        /// </summary>
        public static CMqConsumeResult NotConsumed()
            => new(CMqConsumptionDisposition.NotConsumed);

        /// <summary>
        /// Shortcut function that returns a CMqConsumeResult with the NotConsumed disposition.
        /// </summary>
        public static CMqConsumeResult NotInterested()
            => new(CMqConsumptionDisposition.NotInterested);

        /// <summary>
        /// Shortcut function that returns a CMqConsumeResult with the DeadLetter disposition.
        /// </summary>
        public static CMqConsumeResult DeadLetter()
            => new(CMqConsumptionDisposition.DeadLetter);

        /// <summary>
        /// Shortcut function that returns a CMqConsumeResult with the Drop disposition.
        /// </summary>
        public static CMqConsumeResult Drop()
            => new(CMqConsumptionDisposition.Drop);

        /// <summary>
        /// Shortcut function that returns a CMqConsumeResult with the Defer disposition.
        /// </summary>
        public static CMqConsumeResult Defer(TimeSpan deferDuration)
            => new(CMqConsumptionDisposition.Defer) { DeferDuration = deferDuration };
    }
}
