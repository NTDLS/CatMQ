using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Shared;

namespace Test.QueueClientAutoReconnect
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

            var config = new CMqClientConfiguration
            {
                AutoReconnect = true
            };

            var client = new CMqClient(config); //Create an instance of the client.

            client.OnConnected += (s, e) =>
            {
                Console.WriteLine("Connected...");

                Task.Run(async () =>
                {
                    //Create a queue. These are highly configurable.
                    client.CreateQueue(new CMqQueueConfiguration("MyFirstQueue")
                    {
                        PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                        ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber,
                        AsycnronousDelivery = true,
                        MaxOutstandingDeliveries = 5,
                        DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration()
                        {
                            PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                            MaxMessageAge = TimeSpan.FromMinutes(30)
                        }
                    });
                    Console.WriteLine("Queue created...");

                    client.Subscribe("MyFirstQueue", OnMessageReceived);
                    Console.WriteLine("Subscribed...");
                });
            };

            client.Connect("127.0.0.1", 45784);

            for (int t = 0; t < 10; t++)
            {
                new Thread(async () =>
                {
                    for (int i = 0; i < 10000;)
                    {
                        //Here we are allowing for the client to be disconected to test auto-reconnect.
                        if (client.IsConnected)
                        {
                            try
                            {
                                Console.WriteLine($"Enqueuing message {i:n0}...");
                                await client.EnqueueAsync("MyFirstQueue", new MyMessage($"Test message {i:n0}"));
                                i++;
                            }
                            catch(Exception ex)
                            {
                                Console.WriteLine($"Failed to enqueue message {i:n0}: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Not connected, skipping enqueue...");
                            Thread.Sleep(1000);
                        }
                    }
                    
                }).Start();
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
                //Console.WriteLine($"Received: {myMessage.Text.Length:n0} bytes");
            }
            else
            {
                //Console.WriteLine($"Received: '{message.ObjectType}'->'{message.MessageJson}'");
            }

            //Thread.Sleep(20); //Simulate some processing time.

            return new CMqConsumeResult(CMqConsumptionDisposition.Consumed);

            /*
            if (rawMessage.DeferredCount > 2)
            {
                return new CMqConsumeResult(CMqConsumptionDisposition.Consumed);
            }

            return new CMqConsumeResult(CMqConsumptionDisposition.Defer)
            {
                DeferDuration = (rawMessage.DeferDuration ?? TimeSpan.Zero) + TimeSpan.FromSeconds(1)
            };
            */
        }
    }
}
