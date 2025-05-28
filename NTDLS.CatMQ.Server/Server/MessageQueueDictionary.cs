namespace NTDLS.CatMQ.Server.Server
{
    internal class MessageQueueDictionary
        : Dictionary<string, SingleMessageQueueServer>
    {
        public MessageQueueDictionary()
            : base(StringComparer.InvariantCultureIgnoreCase)
        {
        }
    }
}
