namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// Contains a boxed message which has not been deserialized.
    /// </summary>
    public class CMqBoxedMessage(string objectType , string message)
    {
        /// <summary>
        /// The name of the type enclosed in the message.
        /// </summary>
        public string ObjectType { get; set; } = objectType;
        /// <summary>
        /// The message which was received.
        /// </summary>
        public string Message { get; set; } = message;
    }
}
