using RocksDbSharp;

namespace NTDLS.CatMQ.Server.Server
{
    internal class EnqueuedMessageContainer
    {
        public List<EnqueuedMessage> Messages { get; set; } = new();

        public RocksDb? Database { get; set; }
    }
}
