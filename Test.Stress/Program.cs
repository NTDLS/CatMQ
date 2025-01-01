﻿using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Shared;

namespace Test.Stress
{
    internal class Program
    {
        private static readonly Random _random = new();

        internal class MyMessage(string text) : ICMqMessage
        {
            public string Text { get; set; } = text;
        }

        static void Main()
        {
            Thread.Sleep(5000);

            for (int i = 0; i < 32; i++)
            {
                new Thread(() => InstanceThread()).Start();
            }
        }

        static void InstanceThread()
        {
            Console.WriteLine("Starting client...");

            var client = new CMqClient(new CMqClientConfiguration
            {
                AutoReconnect = true
            });
            client.ConnectAsync("127.0.0.1", 45784);

            Console.WriteLine("Waiting for connection...");
            while (client.IsConnected == false)
            {
                Thread.Sleep(1);
            }

            Console.WriteLine("Connected...");

            var myQueueNames = new HashSet<string>();

            var alreadySubscribedQueueNames = new HashSet<string>();

            int numberOfQueuesToCreate = _random.Next(3, 10); //May create less depending on whether we push duplicates to the HashSet.
            for (int i = 0; i < numberOfQueuesToCreate; i++)
            {
                var queueName = "MQ_" + _random.Next(0, 10);
                myQueueNames.Add(queueName);

                Console.WriteLine($"Creating queue: '{queueName}'.");
                client.CreateQueue(new CMqQueueConfiguration(queueName)
                {
                    PersistenceScheme = CMqPersistenceScheme.Persistent
                });

                if (_random.Next(1, 100) > 50) //We don't always subscribe to our own queue.
                {
                    if (alreadySubscribedQueueNames.Contains(queueName) == false)
                    {
                        Console.WriteLine($"Subscribing to queue: '{queueName}'.");
                        client.Subscribe(queueName, OnMessageReceived);
                        alreadySubscribedQueueNames.Add(queueName);
                    }
                }
            }

            int clientId = Math.Abs(Guid.NewGuid().GetHashCode());

            int messageNumber = 0;
            while (messageNumber < 100000) //Send test messages as objects that inherit from IMqMessage
            {
                foreach (var queueName in myQueueNames)
                {
                    client.Enqueue(queueName, new MyMessage($"Test message {messageNumber++:n0} from {clientId}"));
                }
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
                Console.WriteLine($"Received unknown message type.");
            }
            return true;
        }
    }
}
