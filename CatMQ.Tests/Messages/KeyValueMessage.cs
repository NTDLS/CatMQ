using NTDLS.CatMQ.Shared;

namespace CatMQ.Tests.Messages
{
    public class KeyValueMessage :
        ICMqMessage
    {
        public int Key { get; set; }
        public string Value { get; set; } = string.Empty;

        public KeyValueMessage()
        {
        }

        public KeyValueMessage(int key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
