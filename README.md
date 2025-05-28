# CatMQ
CatMQ is a high-performance and reliable persistent message queue designed for efficient inter-process communication, task queuing, load balancing, and data buffering over TCP/IP.

## Testing Status
[![Regression Tests](https://github.com/NTDLS/CatMQ/actions/workflows/Regression%20Tests.yml/badge.svg)](https://github.com/NTDLS/CatMQ/actions/workflows/Regression%20Tests.yml)

## Another Message Queue?! Why?
CatMQ is not “fully featured”, as in it does not natively support clustering, it is not multi-protocol (no AMQP nor MQTT), and it is not friendly to non-dot-net subscribers.
Ok, then what’s the deal?
Well, we needed a MQ that was slim, straight forward and free of fat-runtimes such as java or additional dependencies such as erlang. We went on an internet fishing expedition and came up empty.

So, we built one. __Welcome to CatMQ: a reliable yet slim message queue.__

## Packages 📦
- Server Nuget package: https://www.nuget.org/packages/NTDLS.CatMQ.Server
- Client Nuget package: https://www.nuget.org/packages/NTDLS.CatMQ.Client
- Dedicated server install and web UI: https://github.com/NTDLS/CatMQ/releases

## See also:
 - https://github.com/NTDLS/KitKey
 - https://github.com/NTDLS/NTDLS.ReliableMessaging

## Server
Running the server is as simple as downloading and installing the [dedicated CatMQ Service](https://github.com/NTDLS/CatMQ/releases), which is a platform independent service that includes a web management interface.

Alternatively, the server can be run in-process using the nuget package NTDLS.CatMQ.Server.
Running the server in-process is simple and configurable. The server process does not have to be dedicated as it can also be one of the processes that is involved in inner-process-communication.

```csharp
internal class Program
{
    static void Main()
    {
        var server = new CMqServer();

        //Listen for queue clients on port 45784
        server.StartAsync(45784);

        Console.WriteLine("Press [enter] to shutdown.");
        Console.ReadLine();

        server.Stop();
    }
}
```

## Client

With the client, we can interact with the server. Create/delete/purge queues, subscribe
and of course send and receive messages. Messages are sent by simply passing a serializable
class instance that inherits ICMqMessage.


```csharp
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
    client.Subscribe("MyFirstQueue", OnMessageReceived);

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

private static CMqConsumeResult OnMessageReceived(CMqClient client, CMqReceivedMessage rawMessage)
{
    var message = rawMessage.Deserialize();

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
    return new CMqConsumeResult(CMqConsumptionDisposition.Consumed);
}
```


## Web API
When enabled, CatMQ also allows managing queues by the way of Web API, you'll first need to login to the web management UI, create a user and generate a API key. This API key will need to be passed in the "x-catmq-api-Key" header value.

**Currently supported WebAPI calls**
- `Enqueue/{queueName}/{objectType}` [json in body]
- `CreateQueue/{queueName}`
- `CreateQueue` [CMqQueueConfiguration json in body]
- `Purge/{queueName}`
- `DeleteQueue/{queueName}`

### Example creating a queue using default settings with WebAPI via cURL
**URL:** */api/CreateQueue/{queueName}*
```
curl --location --request POST 'http://127.0.0.1:45783/api/CreateQueue/MyDefault' \
--header 'x-catmq-api-Key: kk4IajpGUJHMR1dFlzXmvnt0VlvGhp'
```

### Example creating a queue using custom settings with WebAPI via cURL
**URL:** */api/CreateQueue/{queueName}*
```
curl --location 'http://127.0.0.1:45783/api/CreateQueue' \
--header 'x-catmq-api-Key: kk4IajpGUJHMR1dFlzXmvnt0VlvGhp' \
--header 'Content-Type: application/json' \
--data '{
    "QueueName": "MyQueue",
    "BatchDeliveryInterval": "00:00:00",
    "DeliveryThrottle": "00:00:00",
    "MaxDeliveryAttempts": 5,
    "MaxMessageAge": "01:00:00",
    "ConsumptionScheme": "Delivered",
    "DeliveryScheme": "Random",
    "PersistenceScheme": "Persistent"
}'
```

### Example enqueuing a message with WebAPI via cURL
**URL:** */api/Enqueue/{queueName}/{assemblyQualifiedTypeName}*
```
curl --location 'http://127.0.0.1:45783/api/Enqueue/MyFirstQueue/Test.QueueClient.Program%2BMyMessage%2C%20Test.QueueClient' \
--header 'x-catmq-api-Key: kk4IajpGUJHMR1dFlzXmvnt0VlvGhp' \
--header 'Content-Type: application/json' \
--data '{
    "Text": "This is a test message"
}'
```

## Notes about Assembly Qualified Type names
Messages are automatically deserialized by the QueueClient, so its necessary to provide the fully assembly qualified type name when enqueuing a message.

**Assembly Qualified Type Name format:**
- Test.QueueClient.MyMessage, Test.QueueClient
- Explanation: {ClassName}, {AssemblyName}

**Assembly Qualified Type Name format for nested classes:**
- Test.QueueClient.Program+MyMessage, Test.QueueClient
- Explanation: {EnclosingClass}+{Class}, {AssemblyName}

## Technologies
CatMQ is based heavily on internally built technologies that leverage the works by people
much smarter than me. Eternally grateful to all those for making my development a walk in the park.

- Light-weight thread scheduling with sub-pooling: [NTDLS.DelegateThreadPooling](https://github.com/NTDLS/NTDLS.DelegateThreadPooling).
- Nullabily and formatting: [NTDLS.Helpers](https://github.com/NTDLS/NTDLS.Helpers).
- Based heavily on the standalone in-memory message queue: [NTDLS.MemoryQueue](https://github.com/NTDLS/NTDLS.MemoryQueue).
- Round-trip messaging with compression, encryption, checksum and reliability notifications: [NTDLS.ReliableMessaging](https://github.com/NTDLS/NTDLS.ReliableMessaging).
- Stream framing for packets reconstruction and fragmentation: [NTDLS.StreamFraming](https://github.com/NTDLS/NTDLS.StreamFraming).
- Resource protection and concurrency because threads like to bite: [NTDLS.Semaphore](https://github.com/NTDLS/NTDLS.Semaphore)
- Polymorphic deserialization provided by [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) because Microsoft refused to add it for "reasons".
- Mega-tight communication enabled by [protobuf-net](https://github.com/protobuf-net/protobuf-net).
- Message persistence provided by [rocksdb-sharp](https://github.com/curiosity-ai/rocksdb-sharp).
- Logging, because otherwise we'd be blind: [serilog](https://github.com/serilog/serilog).
- Windows service magic: [Topshelf](https://github.com/Topshelf/Topshelf).

## Screenshots

### Home view
__(yes, that's over 1-billion messages).__ 👀

![image](https://github.com/user-attachments/assets/48ed2204-0efa-4ee0-8b8a-50f557f74d24)

### Queue view
![image](https://github.com/user-attachments/assets/2e636a0c-1955-4940-9f0d-74c8c239e64c)

### Messages view
![image](https://github.com/user-attachments/assets/5782e58e-66f7-4f7b-9d2e-8098cb02e616)

### Message view
![image](https://github.com/user-attachments/assets/4c61a930-4ecb-48cb-aa90-c7bd528c7ae8)

## License
[MIT](https://choosealicense.com/licenses/mit/)
