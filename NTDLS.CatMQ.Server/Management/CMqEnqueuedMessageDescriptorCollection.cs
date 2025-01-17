using System.Collections.ObjectModel;

namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Contains readonly information about messages.
    /// </summary>
    public class CMqEnqueuedMessageDescriptorCollection(List<CMqEnqueuedMessageDescriptor> messages)
    {
        /// <summary>
        /// Messages contained in the descriptor.
        /// </summary>
        public ReadOnlyCollection<CMqEnqueuedMessageDescriptor> Messages { get; set; } = new(messages);

        /// <summary>
        /// The offset of the requested messages.
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// The number of messages requested.
        /// </summary>
        public int Take { get; set; }

        /// <summary>
        /// The number of messages contained in the resulting Messages.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// The count of messages in the queue.
        /// </summary>
        public int QueueDepth { get; set; }
    }
}
