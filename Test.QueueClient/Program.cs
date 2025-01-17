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
            Thread.Sleep(2500);

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
                PersistenceScheme = CMqPersistenceScheme.Persistent,
                ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber,
                DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration()
                {
                    PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                    MaxMessageAge = TimeSpan.FromMinutes(10)
                }
            });

            //Subscribe to the queue we just created.
            //For a simplified sample, this will cause this process to receive the messages we send.

            try
            {
                //Wire up an event to listen for messages.
                client.Subscribe("MyFirstQueue", OnMessageReceived);
                //client.SubscribeBuffered("MyFirstQueue", 1000, TimeSpan.FromMilliseconds(500), OnBatchReceived);
                Console.WriteLine("Subscribed...");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            //Enqueue a few messages, note that the message is just a class and it must inherit from ICMqMessage.
            for (int i = 0; i < 1000; i++)
            {
                //client.Enqueue("MyFirstQueue", new MyMessage($"Test message {i:n0}"), TimeSpan.FromSeconds(60));
                client.Enqueue("MyFirstQueue", new MyMessage($"Test message {i:n0}"));
            }

            Console.WriteLine("Press [enter] to shutdown.");
            Console.ReadLine();

            client.Disconnect();
        }

        private static CMqConsumeResult OnMessageReceived(CMqClient client, CMqReceivedMessage rawMessage)
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

            if (rawMessage.DeferredCount > 2)
            {
                return new CMqConsumeResult(CMqConsumptionDisposition.DeadLetter);
            }

            return new CMqConsumeResult(CMqConsumptionDisposition.Defer)
            {
                DeferDuration = (rawMessage.DeferDuration ?? TimeSpan.Zero) + TimeSpan.FromSeconds(1)
            };
        }

        private static void OnBatchReceived(CMqClient client, List<CMqReceivedMessage> rawMessages)
        {
            Console.WriteLine($"Received: '{rawMessages.Count}'");

            foreach (var rawMessage in rawMessages)
            {
                var message = rawMessage.Deserialize();
                if (message is MyMessage myMessage)
                {
                    //Console.WriteLine($"Received: '{myMessage.Text}'");
                }
                else
                {
                    //Console.WriteLine($"Received: '{message.ObjectType}'->'{message.MessageJson}'");
                }

            }
        }
    }
}
