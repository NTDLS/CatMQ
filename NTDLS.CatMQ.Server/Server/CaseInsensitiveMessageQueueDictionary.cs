namespace NTDLS.CatMQ.Server.Server
{
    internal class CaseInsensitiveMessageQueueDictionary : Dictionary<string, SingleMessageQueueServer>
    {
        public CaseInsensitiveMessageQueueDictionary()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }
    }
}
