using Microsoft.Extensions.Caching.Memory;
using RocksDbSharp;

namespace NTDLS.CatMQ.Server.Server
{
    internal class EnqueuedMessageContainer
    {
        public IMemoryCache Cache { get; set; } = new MemoryCache(new MemoryCacheOptions());

        public List<EnqueuedMessage> Messages { get; set; } = new();

        public RocksDb? Database { get; set; }
    }
}
