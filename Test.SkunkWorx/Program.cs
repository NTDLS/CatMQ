using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Shared;

namespace Test.SkunkWorx
{
    public class Program
    {
        const string baseQueueName = "TestQueue";
        const int queueCount = 10;

        static CMqClient? _client;

        public class TestMessage
            : ICMqMessage
        {
            public DateTime CreatedUTC { get; set; } = DateTime.UtcNow;
            public string Text { get; set; } = string.Empty;

            public TestMessage()
            {
            }

            public TestMessage(string text)
            {
                Text = text;
            }
        }

        public static void Main(string[] args)
        {
            _client = new CMqClient();

            _client.Connect("localhost", CMqDefaults.DEFAULT_LISTEN_PORT);

            for (int queueNumber = 0; queueNumber < queueCount; queueNumber++)
            {
                string queueName = $"{baseQueueName}{queueNumber}";

                var config = new CMqQueueConfiguration()
                {
                    QueueName = queueName,
                    MaxMessageAge = null,
                    DeliveryScheme = CMqDeliveryScheme.Balanced,
                    ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber,
                    PersistenceScheme = CMqPersistenceScheme.Persistent,
                    /*
                    DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration()
                    {
                        MaxMessageAge = null,
                        DeliveryScheme = CMqDeliveryScheme.Balanced,
                        ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber,
                        PersistenceScheme = CMqPersistenceScheme.Persistent,
                    }
                    */
                };
                _client.CreateQueue(config);

                if (queueNumber < queueCount - 1) //Don't subscribe to the last queue so that it can eventually catch all the messages.
                {
                    _client.Subscribe(queueName, (client, message) =>
                    {
                        int queueNumber = Random.Shared.Next(0, queueCount);
                        string queueName = $"{baseQueueName}{queueNumber}";

                        //Note that the message is going to be enqueued before it is dequeued which can lead to the message
                        //  appearing to be duplicated. Additionally, if this test process exits before the "Consumed" response
                        //  is received by the server, then the message will actually be duplicated. That is by design as it is
                        //  exactly what this application is asking the server to do.
                        _client.Enqueue(queueName, message.Deserialize());
                        return CMqConsumeResult.Consumed();
                    });
                }
            }

            Console.WriteLine("Press any key to enqueue messages...");
            _ = Console.ReadLine();

            for (int i = 0; i < 1000; i++)
            {
                int queueNumber = Random.Shared.Next(0, queueCount);
                string queueName = $"{baseQueueName}{queueNumber}";

                var message = new TestMessage($"Hello world {i}");
                _client.Enqueue(queueName, message);
            }


            Console.WriteLine("Press any key to unsubscribe...");
            _ = Console.ReadLine();

            for (int queueNumber = 0; queueNumber < queueCount; queueNumber++)
            {
                string queueName = $"{baseQueueName}{queueNumber}";
                try
                {
                    _client.Unsubscribe(queueName);
                }
                catch (Exception ex)
                {
                }
            }
        }
    }
}
