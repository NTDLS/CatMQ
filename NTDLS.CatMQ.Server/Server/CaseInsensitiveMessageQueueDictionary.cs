namespace NTDLS.CatMQ.Server.Server
{
    internal class CaseInsensitiveMessageQueueDictionary : Dictionary<string, MessageQueue>
    {
        public CaseInsensitiveMessageQueueDictionary()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }
    }
}
