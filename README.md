# CatMQ
CatMQ is a high-performance and reliable persistent message queue designed for efficient inter-process communication, task queuing, load balancing, and data buffering.

Applications written in any language can publish and consume messages. .NET applications connect with the native TCP client,
and Python, JavaScript, Go, Java, shell scripts and everything else use the [HTTP API](#http-api). They all share the same
queues, so a message published from one language can be consumed in another.

## Testing Status
[![Regression Tests](https://github.com/NTDLS/CatMQ/actions/workflows/Regression%20Tests.yml/badge.svg)](https://github.com/NTDLS/CatMQ/actions/workflows/Regression%20Tests.yml)

## Another Message Queue?! Why?
CatMQ is not “fully featured”, as in it does not natively support clustering and it is not multi-protocol (no AMQP nor MQTT).
Ok, then what’s the deal?
Well, we needed a MQ that was slim, straight forward and free of fat-runtimes such as java or additional dependencies such as erlang. We went on an internet fishing expedition and came up empty.

So, we built one. __Welcome to CatMQ: a reliable yet slim message queue.__

## Packages 📦
- Dedicated server install, with the web UI and the HTTP API: https://github.com/NTDLS/CatMQ/releases
- HTTP API for any language, described by an OpenAPI document: [docs/api/catmq.openapi.yaml](docs/api/catmq.openapi.yaml)
- .NET client Nuget package: https://www.nuget.org/packages/NTDLS.CatMQ.Client
- .NET server Nuget package, for running the server in-process: https://www.nuget.org/packages/NTDLS.CatMQ.Server

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

## .NET client

With the .NET client, we can interact with the server. Create/delete/purge queues, subscribe
and of course send and receive messages. Messages are sent by simply passing a serializable
class instance that inherits ICMqMessage. For other languages, see the [HTTP API](#http-api).


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

    //Subscribe to the queue we just created. The handler is asynchronous, so no thread is held while it waits.
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

private static async Task<CMqConsumeResult> OnMessageReceived(CMqClient client, CMqReceivedMessage rawMessage)
{
    var message = rawMessage.Deserialize();

    //Here we receive the messages for the queue(s) we are subscribed to
    //  and we can use pattern matching to determine what message was received.
    if (message is MyMessage myMessage)
    {
        await Console.Out.WriteLineAsync($"Received: '{myMessage.Text}'");
    }
    else
    {
        await Console.Out.WriteLineAsync("Received unknown message type.");
    }
    return CMqConsumeResult.Consumed();
}
```

### Use asynchronous handlers
Subscribe with an asynchronous handler, as above, and `await` anything that waits: database calls, HTTP requests, file access and
delays. Several messages are delivered at once (up to the queue's `MaxOutstandingDeliveries`), and an asynchronous handler holds no
thread while it waits, however slow the work is.

A synchronous handler holds a thread-pool thread for as long as it runs. A few slow synchronous handlers can use up the pool's
threads, and while they are waiting, everything else in the process that needs a pool thread waits too, including the client's
own replies to the server. Deliveries then time out and fail even though the handler did its work. Synchronous handlers are
fine for work that never waits, and never block on a task (`.Result`, `.Wait()`) inside one.
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

A queue accepts messages without limit unless it has a `MaxDepth`. Once a queue holds that many messages, publishing
to it fails with `CMqQueueFullException`, which tells publishers to slow down rather than letting the queue grow until
the server runs out of memory or disk. Set one on any queue whose publishers could outpace its consumers, especially an
ephemeral queue, which keeps every message in memory. `EnqueueExpedient` does not wait for a reply, so it is not told
when a queue is full and the message is lost. Dead-letter queues are always unlimited.

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

## TCP and HTTP
.NET applications have a dedicated path: the native TCP client, which pushes messages to them as they arrive. Every other
language uses the HTTP API, which .NET applications can use as well. Both reach the same queues, so a .NET publisher can
feed a Python consumer and the other way around.

| | Native TCP (.NET) | HTTP API (any language) |
|---|---|---|
| Receiving | Pushed to a handler as messages arrive | Asked for with long-polling, then acknowledged |
| Publishing many at once | One call per message | Batches of up to 1,000 per request |
| Consumer identity | Ends when the connection does | Named, and kept across restarts |
| Needs | The NuGet client | Any HTTP client |

### Performance
These were measured against the CatMQ service on one machine (Intel Core Ultra 7 155H, 22 logical processors, Windows),
with the benchmark in a separate process connecting over localhost, persistent queues and 500-byte messages. The service
and clients used their default settings: Deflate `Fastest` compression for frames of 1 KB or more (so these 500-byte
messages were sent uncompressed) and Nagle's algorithm off. Where a range is shown, it is the lowest and highest of two runs.
Treat them as a rough comparison, not a guarantee: a network, other hardware or larger messages will change them.

| Publishing | Messages per second |
|---|---|
| TCP `Enqueue`, 1 client | 6,600 to 7,500 |
| TCP `Enqueue`, 8 clients | 32,100 to 33,800 |
| TCP `EnqueueExpedient` (no confirmation), 1 client | 43,100 to 56,900 |
| HTTP, one message per request, 1 at a time | 4,000 to 4,800 |
| HTTP, one message per request, 8 at a time | 24,100 to 24,800 |
| HTTP, batches of 100, 1 at a time | 53,100 to 60,200 |
| HTTP, batches of 100, 4 at a time | 118,400 to 126,900 |

| Consuming (handler or consumer does no work) | Messages per second |
|---|---|
| TCP subscriber, `MaxOutstandingDeliveries` 5 | 10,800 to 14,000 |
| TCP subscriber, `MaxOutstandingDeliveries` 64 | 11,900 to 14,400 |
| HTTP, 1 consumer receiving up to 5, limit 5 | 10,600 to 10,800 |
| HTTP, 4 consumers receiving up to 16 each, limit 64 | 16,900 to 17,000 |

| Latency, from publish to the consumer having the message | Median | 99th percentile |
|---|---|---|
| TCP publisher to TCP subscriber | 0.7 ms | 1.6 to 2.3 ms |
| HTTP publisher to an HTTP consumer that is already waiting | 1.8 ms | 10.8 to 11.8 ms |

Larger JSON messages are compressed. For JSON of 50 KB or more, `Fastest` makes them roughly twice the size that `Optimal`
would, in about a fifth of the time (for 4 KB, about 1.5 times the size in 40% of the time), which suits a fast local network. On a slower link, set `CompressionLevel` to `Optimal` on the client and the server.
They do not need to match, because each frame records how it was compressed.

In practice, a consumer's own work usually limits throughput long before either protocol does, and
`MaxOutstandingDeliveries` decides how much of that work happens at once.

## HTTP API
The CatMQ service has an HTTP API for managing queues and for publishing and consuming messages from any language. The full
contract is an OpenAPI document: [docs/api/catmq.openapi.yaml](docs/api/catmq.openapi.yaml), from which clients can be generated.
The API is on by default, and can be turned off with `EnableWebApi` in the service's configuration.

You'll first need to log in to the web management UI, create a user and generate an API key. Pass the key in the
`Authorization: Bearer` header, or in the `x-catmq-api-key` header. The health check at `/api/v1/health` needs no key.

### Creating a queue
`PUT` creates the queue, or applies these settings if it already exists. Settings that are left out return to their
defaults, so use `PATCH` to change only some of them.
```bash
curl -X PUT http://127.0.0.1:45783/api/v1/queues/orders -H "Authorization: Bearer $CATMQ_KEY" -H "Content-Type: application/json" -d '{"persistence":"persistent","consumptionScheme":"firstConsumer","maxOutstandingDeliveries":5}'
```

### Publishing
A JSON body is sent as ordinary JSON. Other text goes in `body` with a `contentType`, and binary content goes in `bodyBase64`.
```bash
curl -X POST http://127.0.0.1:45783/api/v1/queues/orders/messages -H "Authorization: Bearer $CATMQ_KEY" -H "Content-Type: application/json" -d '{"messageType":"orders.created","body":{"orderId":1234},"idempotencyKey":"order-1234"}'
```

To publish many at once, send `{"messages": [...]}`. The response reports each message separately. Publishing again
with the same `idempotencyKey` within 24 hours returns the original message rather than adding another, which makes
retries safe. Keys are held in memory, so they do not survive a restart.

A queue with a `maxDepth` refuses messages once it is full, with `429` and the error code `queue_full`, so publishers
should wait for the number of seconds in the `Retry-After` header and try again. In a batch, each message that does not
fit is rejected individually. A queue without a `maxDepth` never refuses a message.

### Consuming
A consumer asks for messages, works on them, then acknowledges each one. `receive` waits up to `waitSeconds` for a
message to arrive, and returns an empty list if none does. Each message is leased to the consumer for `leaseSeconds`.
```bash
curl -X POST http://127.0.0.1:45783/api/v1/queues/orders/messages/receive -H "Authorization: Bearer $CATMQ_KEY" -H "Content-Type: application/json" -d '{"consumer":"billing","max":10,"waitSeconds":20,"leaseSeconds":60}'
```

```bash
curl -X POST http://127.0.0.1:45783/api/v1/leases/LEASE_ID/ack -H "Authorization: Bearer $CATMQ_KEY" -H "Content-Type: application/json" -d '{"disposition":"consumed"}'
```

The disposition can be `consumed`, `notInterested`, `notConsumed`, `defer` (with `deferSeconds`), `deadLetter` or `drop`.
A consumer that needs more time can extend a lease with `POST /api/v1/leases/{leaseId}/extend`.

Delivery is **at-least-once**. A message whose lease runs out goes back to the queue and counts as a failed delivery
attempt, and an unacknowledged message is also delivered again after a restart. Use `messageId`, which never changes,
to recognize a message that was already handled. Leased messages count toward the queue's `maxOutstandingDeliveries`,
so a single `receive` returns no more messages than the queue has free delivery slots.

A consumer is identified by its name, and requests that use the same name compete for messages. Consumers stay
registered across restarts. With `allConsumers`, messages wait until every registered consumer has them, so remove a
consumer that has gone away for good with `DELETE /api/v1/queues/{queue}/consumers/{consumer}`.

### A complete consumer in Python
Using only the standard library:
```python
import json, os, urllib.request

BASE = "http://127.0.0.1:45783/api/v1"
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
await fetch("http://127.0.0.1:45783/api/v1/queues/orders/messages", {
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
