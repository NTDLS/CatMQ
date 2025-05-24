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

        public CMqConsumeResult(CMqConsumptionDisposition disposition)
        {
            Disposition = disposition;
        }

        /// <summary>
        /// Shortcut function that returns a CMqConsumeResult with the specified disposition.
        /// </summary>
        public static CMqConsumeResult FromDisposition(CMqConsumptionDisposition disposition)
        {
            return new CMqConsumeResult(disposition);
        }
    }
}
