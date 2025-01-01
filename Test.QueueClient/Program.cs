using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Shared;

namespace Test.QueueClient
{
    internal class Program
    {
        internal class MyMessage(string text) : ICMqMessage
        {
            public string Text { get; set; } = text;
        }

        static void Main()
        {
            var client = new CMqClient(); //Create an instance of the client.
            client.Connect("127.0.0.1", 45784); //Connect to the queue server.

            Console.WriteLine("Waiting for connection...");
            while (client.IsConnected == false)
            {
                Thread.Sleep(1);
            }

            Console.WriteLine("Connected...");

            //Create a queue. These are highly configurable.
            client.CreateQueue(new CMqQueueConfiguration("MyFirstQueue")
            {
                PersistenceScheme = CMqPersistenceScheme.Ephemeral
            });

            //Subscribe to the queue we just created.
            //For a simplified sample, this will cause this process to receive the messages we send.

            try
            {
                //Wire up an event to listen for messages.
                client.Subscribe("MyFirstQueue", OnMessageReceived);
                Console.WriteLine("Subscribed...");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            //Enqueue a few messages, note that the message is just a class and it must inherit from ICMqMessage.
            for (int i = 0; i < 10000000; i++)
            {
                client.Enqueue("MyFirstQueue", new MyMessage($"Test message {i++:n0}"));
            }

            Console.WriteLine("Press [enter] to shutdown.");
            Console.ReadLine();

            //Cleanup.
            client.Disconnect();
        }

        private static bool OnMessageReceived(CMqClient client, CMqReceivedMessage rawMessage)
        {
            var message = rawMessage.Deserialize();
            if (message is MyMessage myMessage)
            {
                Console.WriteLine($"Received: '{myMessage.Text}'");
            }
            else
            {
                //Console.WriteLine($"Received: '{message.ObjectType}'->'{message.MessageJson}'");
            }

            return true;
        }
    }
}
