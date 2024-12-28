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
            client.OnReceived += Client_OnReceived; //Wire up an event to listen for messages.

            //Create a queue. These are highly configurable.
            client.CreateQueue(new CMqQueueConfiguration("MyFirstQueue")
            {
                Persistence = PMqPersistence.Ephemeral
            });

            //Subscribe to the queue we just created.
            //For a simplified sample, this will cause this process to receive the messages we send.
            client.Subscribe("MyFirstQueue");

            //Enqueue a few messages, note that the message is just a class and it must inherit from ICMqMessage.
            for (int i = 0; i < 10; i++)
            {
                client.Enqueue("MyFirstQueue", new MyMessage($"Test message {i++:n0}"));
            }

            Console.WriteLine("Press [enter] to shutdown.");
            Console.ReadLine();

            //Cleanup.
            client.Disconnect();
        }

        private static bool Client_OnReceived(CMqClient client, string queueName, ICMqMessage message)
        {
            //Here we receive the messages for the queue(s) we are subscribed to
            //  and we can use pattern matching to determine what message was received.
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
