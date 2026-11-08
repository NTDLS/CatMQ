# CatMQ for Python

A client for the [CatMQ](https://github.com/NTDLS/CatMQ) HTTP API. It uses only the standard library and runs on Python 3.8 or later.

```bash
pip install ./clients/python
```

## Publish and consume

```python
from catmq import CatMQ

mq = CatMQ("http://localhost:45783", api_key="...")

mq.create_queue("orders", persistence="persistent", consumption_scheme="firstConsumer",
                dead_letter={"persistence": "persistent"})

mq.publish("orders", {"orderId": 1234}, message_type="orders.created", headers={"tenant": "acme"})
mq.publish("orders", "plain text", content_type="text/plain")
mq.publish("orders", b"\x00\x01binary")      # bytes are sent as binary

def handle(message):
    print(message.message_type, message.body)  # JSON arrives parsed, text as str, binary as bytes
    # Return nothing to acknowledge it, return "deadLetter" or "drop" to settle it another way,
    # or raise to have it delivered again.

mq.consume("orders", "order-worker", handle)   # runs until stop (a threading.Event) is set
```

Every consumer has a name. Consumers that use the same name share the messages; a queue with the `allConsumers` scheme delivers each message to every name.

## Receive and acknowledge yourself

```python
for message in mq.receive("orders", "order-worker", max=10, wait_seconds=20, lease_seconds=60):
    try:
        process(message.body)
        message.ack()
    except TemporaryError:
        message.defer(30)        # try again in 30 seconds, without counting a failed attempt
    except Exception:
        message.nack()           # deliver again, counting a failed attempt
```

A message is leased to you until `lease_seconds` pass; `message.extend(seconds)` keeps it longer. A message that is not acknowledged before its lease expires is delivered again. `mq.ack_batch([...])` acknowledges many leases in one request.

## Batches and idempotency

```python
results = mq.publish_batch("orders", [
    {"body": {"orderId": 1}, "idempotency_key": "order-1"},
    {"body": {"orderId": 2}, "idempotency_key": "order-2", "defer_seconds": 60},
])
for result in results:
    print(result.status, result.message_id)    # accepted, duplicate or rejected (with result.error)
```

Publishing again with an `idempotency_key` CatMQ has seen in the last 24 hours returns `duplicate` and the original message ID, so a publish can be retried safely.

## Errors

Every failure raises `CatMQError`, with the HTTP `status`, CatMQ's `code` (such as `queue_not_found`, `queue_full` or `lease_expired`), a `title`, and `retry_after` when CatMQ gives one. `status` is 0 when CatMQ could not be reached.

```python
from catmq import CatMQError

try:
    mq.publish("orders", {"orderId": 1})
except CatMQError as ex:
    if ex.code == "queue_full":
        ...  # slow down
```

## Webhooks

```python
webhook = mq.put_webhook("orders", "billing", "https://billing.example.com/catmq",
                         headers={"Authorization": "Bearer ..."}, max_concurrency=4)
secret = webhook["secret"]   # returned only when the webhook is created; store it
```

In the receiver, check each delivery's signature against the raw body before trusting it:

```python
from catmq import verify_webhook, WebhookSignatureError

def on_request(raw_body: bytes, headers) -> int:
    try:
        delivery = verify_webhook(raw_body, headers.get("X-CatMQ-Signature"), secret)
    except WebhookSignatureError:
        return 401
    process(delivery["body"])
    return 200      # 410 drops it, 422 dead-letters it, 429/503 defer it (Retry-After), anything else retries
```

`mq.rotate_webhook_secret(queue, name)` returns a new secret; for 24 hours deliveries are signed with both, and `verify_webhook` accepts a list of secrets while you switch.

## Everything else

| Area | Methods |
|---|---|
| Queues | `list_queues`, `iter_queues`, `create_queue`, `get_queue`, `update_queue`, `delete_queue`, `purge_queue`, `redrive` |
| Messages | `publish`, `publish_batch`, `list_messages`, `iter_messages`, `get_message`, `delete_message` |
| Leases | `receive`, `ack`, `ack_batch`, `extend`, `consume` |
| Consumers | `list_consumers`, `delete_consumer` |
| Webhooks | `list_webhooks`, `put_webhook`, `get_webhook`, `delete_webhook`, `rotate_webhook_secret`, `verify_webhook` |
| System | `health` |

Settings can be given in snake_case (`max_depth`) or as the API names them (`maxDepth`). Results are the API's JSON, as dicts. The full API is described in [docs/api/catmq.openapi.yaml](../../docs/api/catmq.openapi.yaml).

## Tests

`tests/conformance.py` runs the shared scenarios in [clients/conformance](../conformance/scenarios.json) against a running service. The CatMQ test suite runs it against a real service as part of `dotnet test`.

```bash
CATMQ_URL=http://127.0.0.1:45783 CATMQ_API_KEY=... python tests/conformance.py
```
