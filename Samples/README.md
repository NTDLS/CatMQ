# CatMQ samples

Each sample publishes and consumes the same messages on the same queue, so any publisher can feed any consumer: publish
from Python and consume in .NET, publish over TCP and consume from JavaScript, and so on.

| Sample | Publishes with | Consumes with |
|---|---|---|
| [dotnet-tcp](dotnet-tcp) | The .NET client's `Enqueue`, typed and with a message type and body | `Subscribe`, as a named consumer |
| [dotnet-http](dotnet-http) | The HTTP API, with `HttpClient` and no CatMQ package | `receive`, then acknowledging the leases |
| [python](python) | The Python client's `publish` and `publish_batch` | `consume`, or `receive` and `ack` |
| [javascript](javascript) | The JavaScript client's `publish` and `publishBatch` | `consume`, or `receive` and `ack` |

## The messages
Every sample uses the queue `samples.orders` (persistent, first consumer wins, with a dead-letter queue) and publishes:

| Message type | Content type | Body |
|---|---|---|
| `orders.created` | `application/json` | `{"orderId": 1, "customer": "Ada", "total": 42.5}`, with a `source` header naming the sample |
| `orders.note` | `text/plain` | A line of text |
| `orders.receipt` | `application/octet-stream` | A few bytes of binary |

One `orders.created` message has no customer. Every consumer dead-letters it, so it ends up in `samples.orders.dlq`.

## Running them
Start the CatMQ service, then in the web UI create an API key (the HTTP samples need it; TCP needs none). Then, for example:

```bash
export CATMQ_API_KEY=your-api-key
python samples/python/publisher.py 5
dotnet run --project samples/dotnet-tcp -- consume --until-idle
```

| Setting | Default | Used by |
|---|---|---|
| `CATMQ_URL` | `http://127.0.0.1:45783` | dotnet-http, python, javascript |
| `CATMQ_API_KEY` | (none) | dotnet-http, python, javascript |
| `CATMQ_HOST` | `127.0.0.1` | dotnet-tcp |
| `CATMQ_PORT` | `45784` | dotnet-tcp |

Each consumer runs until you press Ctrl+C. With `--until-idle`, it stops once the queue has been empty for a moment.
