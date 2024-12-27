using NTDLS.CatMQClient;
using NTDLS.CatMQShared;

namespace TestHarness
{
    internal class Program
    {
        internal class MyMessage(string text) : ICMqMessage
        {
            public string Text { get; set; } = text;
        }

        static void Main()
        {
            var client = new CMqClient();
            client.Connect("127.0.0.1", 45784);
            client.CreateQueue(new CMqQueueConfiguration("MyFirstQueue")
            {
                Persistence = PMqPersistence.Persistent
            });
            client.Subscribe("MyFirstQueue");
            client.OnReceived += Client_OnReceived;

            for (int i = 0; i < 10; i++)//Send test messages as objects that inherit from IMqMessage
            {
                client.Enqueue("MyFirstQueue", new MyMessage($"Test message {i:n0}"));
            }

            Console.WriteLine("Press [enter] to shutdown.");
            Console.ReadLine();

            client.OnReceived -= Client_OnReceived;

            //Cleanup.
            client.Disconnect();
        }

        private static bool Client_OnReceived(CMqClient client, string queueName, ICMqMessage message)
        {
            if (message is MyMessage myMessage)
            {
                Console.WriteLine($"Received: '{myMessage.Text}'");
            }
            else
            {
                Console.WriteLine($"Received unknown message type.");
            }
            return true;
        }
    }
}