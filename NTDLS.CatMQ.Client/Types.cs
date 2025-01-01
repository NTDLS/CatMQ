using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Client
{
    /// <summary>
    /// Delegate used for server-to-client delivery notifications containing raw JSON.
    /// </summary>
    /// <returns>Return true to mark the message as consumed by the client.</returns>
    public delegate CMqConsumptionResult OnReceivedEvent(CMqClient client, CMqReceivedMessage rawMessage);
}
