# CatMQ
CatMQ is a high-performance and reliable persistent message queue designed for efficient inter-process communication, task queuing, load balancing, and data buffering over TCP/IP.

## Packages 📦
- Server Nuget package: https://www.nuget.org/packages/NTDLS.CatMQ.Server
- Client Nuget package: https://www.nuget.org/packages/NTDLS.CatMQ.Client
- Dedicated server install and web UI: https://github.com/NTDLS/CatMQ/releases

## Running the server
The server can either be run in-process using the nuget package NTDLS.CatMQ.Server or by downloading
and installing the [dedicated CatMQ Service](https://github.com/NTDLS/CatMQ/releases), which is a platform
independent service that includes a web management interface.

### Server
Running the server is quite simple, but configurable. The server does not have to be dedicated either,
it can be one of the process that is involved in inner-process-communication.

Alternatively, you can just install the dedicated server which includes a management web UI.

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

## Connecting a client to a server
With the client, we can interact with the server. Create/delete/purge queues, subscribe
and of course send and received messages. Messages are sent by simply passing a serializable
class that inherits from ICMqMessage.


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
```

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
![image](https://github.com/user-attachments/assets/992d0278-f5e3-4830-af6b-52637812714e)

### Queue view
![image](https://github.com/user-attachments/assets/bf9387ec-8a7e-4847-9d96-ba18cfc66cf6)

### Messages view
![image](https://github.com/user-attachments/assets/2e3ebc0f-7b67-41b6-b398-2c7a5aad7f8f)

### Message view
![image](https://github.com/user-attachments/assets/d43b89a6-398a-41d3-84dc-6a8b5e4b84de)


## License
[MIT](https://choosealicense.com/licenses/mit/)
