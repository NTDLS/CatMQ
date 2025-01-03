using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// We use this class to save a queues meta-data to disk.
    /// </summary>
    internal class MessageQueueMetadata
    {
        public CMqQueueConfiguration Configuration { get; set; } = new();
        public MessageQueueStatistics Statistics { get; set; } = new();

        public MessageQueueMetadata(CMqQueueConfiguration configuration, MessageQueueStatistics statistics)
        {
            Configuration = configuration;
            Statistics = statistics;
        }

        public MessageQueueMetadata()
        {
        }
    }
}
