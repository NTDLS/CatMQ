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
        public TimeSpan? DifferedDuration { get; set; }

        public CMqConsumeResult(CMqConsumptionDisposition disposition)
        {
            Disposition = disposition;
        }
    }
}
