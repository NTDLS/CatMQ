# CatMQ
CatMQ is a high-performance and reliable persistent message queue designed for efficient inter-process communication, task queuing, load balancing, and data buffering over TCP/IP.

## Testing Status
[![Regression Tests](https://github.com/NTDLS/CatMQ/actions/workflows/Regression%20Tests.yml/badge.svg)](https://github.com/NTDLS/CatMQ/actions/workflows/Regression%20Tests.yml)

## Another Message Queue?! Why?
CatMQ is not “fully featured”, as in it does not natively support clustering and it is not multi-protocol (no AMQP nor MQTT).
.NET applications use the native TCP client, and anything else can publish and consume over the [HTTP API](#http-api).
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

## Management UI
<img width="1465" height="1021" alt="image" src="https://github.com/user-attachments/assets/ede6ca47-c6e5-4240-b5d1-d654e5b7b17d" />

## Server
Running the server is as simple as downloading and installing the [dedicated CatMQ Service](https://github.com/NTDLS/CatMQ/releases), which is a platform independent service that includes a web management interface.

Alternatively, the server can be run in-process using the nuget package NTDLS.CatMQ.Server.
Running the server in-process is simple and configurable. The server process does not have to be dedicated as it can also be one of the processes that is involved in inner-process-communication.

```csharp
internal class Program
{
    static void Main()
    {
        //A persistence path is only needed for persistent queues.
        var server = new CMqServer(new CMqServerConfiguration { PersistencePath = "C:\\CatMQ" });

        //Listen for queue clients on port 45784
        server.Start(45784);

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

    //Create a queue. These are highly configurable.
    client.CreateQueue(new CMqQueueConfiguration("MyFirstQueue")
    {
        PersistenceScheme = CMqPersistenceScheme.Ephemeral
    });

    //Subscribe to the queue we just created.
    client.Subscribe("MyFirstQueue", OnMessageReceived);

    //Enqueue a few messages, note that the message is just a class and it must inherit from ICMqMessage.
    for (int i = 0; i < 10; i++)
    {
        client.Enqueue("MyFirstQueue", new MyMessage($"Test message {i:n0}"));
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
    return CMqConsumeResult.Consumed();
}
```

If the handler awaits I/O (database calls, HTTP, etc.), subscribe with an asynchronous handler instead. No thread is held while it is pending,
so slow handlers cannot starve the thread-pool. Avoid blocking on tasks (.Result, .Wait()) inside a synchronous handler for the same reason.
```csharp
client.Subscribe("MyFirstQueue", async (client, rawMessage) =>
{
    await SaveToDatabaseAsync(rawMessage.Deserialize());
    return CMqConsumeResult.Consumed();
});
```

### Creating and changing queues
`CreateQueue` is safe to call every time an application starts. With a configuration, it creates the queue, or applies
the configuration to the queue if it already exists, and the change takes effect immediately. With only a name, it
creates the queue with default settings if it does not exist, and otherwise leaves it alone.
The `PersistenceScheme` of an existing queue cannot be changed.

### Message types
A message's type is just a name. By default the .NET client sends the class's assembly-qualified name, so that
`Deserialize()` can recreate it. To use a name that other languages can work with as well, register one:
```csharp
CMqSerialization.RegisterMessageType<MyMessage>("my.message");
```

Messages from other publishers, whose type is not a .NET class, can be read by naming the class to deserialize to.
Property names are matched without regard to case, so `orderId` from a JavaScript publisher fills `OrderId`.
```csharp
var order = rawMessage.Deserialize<MyMessage>();
```

A message can also be published without a .NET class at all, with an optional content type and headers:
```csharp
client.Enqueue("MyFirstQueue", "orders.created", "{\"orderId\":1234}", new CMqEnqueueOptions
{
    ContentType = "application/json",
    Headers = new Dictionary<string, string> { ["tenant"] = "acme" }
});
```

### Named consumers
By default each client is a separate consumer. Clients that share a consumer name act as competing workers instead,
so each message goes to one of them. Clients with a different name still get their own copy when the queue's
`ConsumptionScheme` is `AllSubscribersSatisfied`.
```csharp
var client = new CMqClient(new CMqClientConfiguration { ConsumerName = "billing" });
```

## HTTP API
When enabled, the CatMQ service also has an HTTP API, so that queues can be managed, and messages published and consumed,
from any language. The full contract is an OpenAPI document: [docs/api/catmq-v2.openapi.yaml](docs/api/catmq-v2.openapi.yaml).

You'll first need to log in to the web management UI, create a user and generate an API key. Pass the key in the
`Authorization: Bearer` header, or in the `x-catmq-api-key` header. The health check at `/api/v2/health` needs no key.

### Creating a queue
`PUT` creates the queue, or applies these settings if it already exists. Settings that are left out return to their
defaults, so use `PATCH` to change only some of them.
```bash
curl -X PUT http://127.0.0.1:45783/api/v2/queues/orders -H "Authorization: Bearer $CATMQ_KEY" -H "Content-Type: application/json" -d '{"persistence":"persistent","consumptionScheme":"firstConsumer","maxOutstandingDeliveries":5}'
```

### Publishing
A JSON body is sent as ordinary JSON. Other text goes in `body` with a `contentType`, and binary content goes in `bodyBase64`.
```bash
curl -X POST http://127.0.0.1:45783/api/v2/queues/orders/messages -H "Authorization: Bearer $CATMQ_KEY" -H "Content-Type: application/json" -d '{"messageType":"orders.created","body":{"orderId":1234},"idempotencyKey":"order-1234"}'
```

To publish many at once, send `{"messages": [...]}`. The response reports each message separately. Publishing again
with the same `idempotencyKey` within 24 hours returns the original message rather than adding another, which makes
retries safe. Keys are held in memory, so they do not survive a restart.

### Consuming
A consumer asks for messages, works on them, then acknowledges each one. `receive` waits up to `waitSeconds` for a
message to arrive, and returns an empty list if none does. Each message is leased to the consumer for `leaseSeconds`.
```bash
curl -X POST http://127.0.0.1:45783/api/v2/queues/orders/messages/receive -H "Authorization: Bearer $CATMQ_KEY" -H "Content-Type: application/json" -d '{"consumer":"billing","max":10,"waitSeconds":20,"leaseSeconds":60}'
```

```bash
curl -X POST http://127.0.0.1:45783/api/v2/leases/LEASE_ID/ack -H "Authorization: Bearer $CATMQ_KEY" -H "Content-Type: application/json" -d '{"disposition":"consumed"}'
```

The disposition can be `consumed`, `notInterested`, `notConsumed`, `defer` (with `deferSeconds`), `deadLetter` or `drop`.
A consumer that needs more time can extend a lease with `POST /api/v2/leases/{leaseId}/extend`.

Delivery is **at-least-once**. A message whose lease runs out goes back to the queue and counts as a failed delivery
attempt, and an unacknowledged message is also delivered again after a restart. Use `messageId`, which never changes,
to recognize a message that was already handled. Leased messages count toward the queue's `maxOutstandingDeliveries`,
so a single `receive` returns no more messages than the queue has free delivery slots.

A consumer is identified by its name, and requests that use the same name compete for messages. Consumers stay
registered across restarts. With `allConsumers`, messages wait until every registered consumer has them, so remove a
consumer that has gone away for good with `DELETE /api/v2/queues/{queue}/consumers/{consumer}`.

### A complete consumer in Python
Using only the standard library:
```python
import json, os, urllib.request

BASE = "http://127.0.0.1:45783/api/v2"
KEY = os.environ["CATMQ_KEY"]

def call(method, path, body=None):
    request = urllib.request.Request(BASE + path, method=method, data=None if body is None else json.dumps(body).encode(),
                                     headers={"Authorization": f"Bearer {KEY}", "Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
        text = response.read()
        return json.loads(text) if text else None

while True:
    batch = call("POST", "/queues/orders/messages/receive", {"consumer": "billing", "max": 10, "waitSeconds": 20})
    for message in batch["messages"]:
        print("order", message["body"]["orderId"])
        call("POST", f"/leases/{message['leaseId']}/ack", {"disposition": "consumed"})
```

### Publishing from JavaScript
```javascript
await fetch("http://127.0.0.1:45783/api/v2/queues/orders/messages", {
    method: "POST",
    headers: { "Authorization": `Bearer ${process.env.CATMQ_KEY}`, "Content-Type": "application/json" },
    body: JSON.stringify({ messageType: "orders.created", body: { orderId: 1234 } })
});
```

### Everything else
The API can also list queues, change some of a queue's settings with `PATCH`, list and delete messages without
consuming them, purge a queue, move messages from a dead-letter queue back onto its queue (`POST /queues/{queue}/redrive`),
and list and remove consumers. Every error is returned as an RFC 9457 problem document with a stable `code`, such as
`queue_not_found` or `lease_expired`.

Webhooks, where CatMQ pushes messages to a URL, are described in the OpenAPI document but are not available yet.

The original API at `/api/v1/queues` is still available for existing integrations, but new ones should use version 2.

## Upgrading from 2.x
- Clients and servers must be upgraded together, because the messages they exchange have changed.
- `CMqReceivedMessage.AssemblyQualifiedTypeName` is now `MessageType`, and `MessageJson` is now `Body`.
- Calling `CreateQueue` with a configuration now changes an existing queue, where before it was ignored. Check that the
  configuration in each application's code is what you want the queue to have.
- Messages that are already persisted keep working, and are delivered as before.

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

### Newer Home Screen
<img width="1327" height="984" alt="image" src="https://github.com/user-attachments/assets/42a8df05-066e-471f-ac6e-d426c4ef23e0" />

### Queue view
<img width="1323" height="897" alt="image" src="https://github.com/user-attachments/assets/2e2ce6b8-f669-4e00-844a-5eb5b5e79e47" />

### Messages view
![image](https://github.com/user-attachments/assets/5782e58e-66f7-4f7b-9d2e-8098cb02e616)

### Message view
![image](https://github.com/user-attachments/assets/4c61a930-4ecb-48cb-aa90-c7bd528c7ae8)

## License
[MIT](https://choosealicense.com/licenses/mit/)
