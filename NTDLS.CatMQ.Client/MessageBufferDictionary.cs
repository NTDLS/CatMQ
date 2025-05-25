using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Client
{
    internal class MessageBufferDictionary
        : Dictionary<Guid, List<CMqReceivedMessage>>
    {
        public MessageBufferDictionary()
        {
        }
    }
}
