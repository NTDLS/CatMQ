using NTDLS.CatMQ.Client;

namespace CatMQ.Tests
{
    public class ClientFactory
    {
        public static CMqClient CreateAndConnect()
        {
            var client = new CMqClient();

            client.OnException += Client_OnException;
            client.OnConnected += Client_OnConnected;
            client.OnDisconnected += Client_OnDisconnected;

            client.Connect(Constants.HOST_NAME, Constants.LISTEN_PORT);

            return client;
        }

        private static void Client_OnDisconnected(NTDLS.CatMQ.Client.CMqClient client)
        {
            Console.WriteLine("Client disconnected.");
        }

        private static void Client_OnConnected(NTDLS.CatMQ.Client.CMqClient client)
        {
            Console.WriteLine("Client connected.");
        }

        private static void Client_OnException(NTDLS.CatMQ.Client.CMqClient client, string? storeKey, Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}
