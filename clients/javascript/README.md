# CatMQ for JavaScript

A client for the [CatMQ](https://github.com/NTDLS/CatMQ) HTTP API. It has no dependencies, includes TypeScript types, and runs on Node.js 20 or later, Deno, Bun and browsers.

```bash
npm install ./clients/javascript
```

## Publish and consume

```js
import { CatMQ } from 'catmq';

const mq = new CatMQ({ url: 'http://localhost:45783', apiKey: process.env.CATMQ_API_KEY });

await mq.createQueue('orders', {
  persistence: 'persistent',
  consumptionScheme: 'firstConsumer',
  deadLetter: { persistence: 'persistent' },
});

await mq.publish('orders', { orderId: 1234 }, { messageType: 'orders.created', headers: { tenant: 'acme' } });
await mq.publish('orders', 'plain text', { contentType: 'text/plain' });
await mq.publish('orders', new Uint8Array([0, 1, 2]));      // bytes are sent as binary

const stop = new AbortController();
await mq.consume('orders', 'order-worker', async (message) => {
  console.log(message.messageType, message.body);  // JSON arrives parsed, text as a string, binary as a Uint8Array
  // Return nothing to acknowledge it, return 'deadLetter' or 'drop' to settle it another way,
  // or throw to have it delivered again.
}, { signal: stop.signal });
```

Every consumer has a name. Consumers that use the same name share the messages; a queue with the `allConsumers` scheme delivers each message to every name.

## Receive and acknowledge yourself

```js
for (const message of await mq.receive('orders', 'order-worker', { max: 10, waitSeconds: 20, leaseSeconds: 60 })) {
  try {
    await process(message.body);
    await message.ack();
  } catch (error) {
    if (error instanceof TemporaryError) await message.defer(30); // try again in 30 seconds, without counting a failed attempt
    else await message.nack();                                     // deliver again, counting a failed attempt
  }
}
```

A message is leased to you until `leaseSeconds` pass; `message.extend(seconds)` keeps it longer. A message that is not acknowledged before its lease expires is delivered again. `mq.ackBatch([...])` acknowledges many leases in one request.

## Batches and idempotency

```js
const results = await mq.publishBatch('orders', [
  { body: { orderId: 1 }, idempotencyKey: 'order-1' },
  { body: { orderId: 2 }, idempotencyKey: 'order-2', deferSeconds: 60 },
]);
for (const { status, messageId, error } of results) console.log(status, messageId, error); // accepted, duplicate or rejected
```

Publishing again with an `idempotencyKey` CatMQ has seen in the last 24 hours returns `duplicate` and the original message ID, so a publish can be retried safely.

## Errors

Every failure throws a `CatMQError`, with the HTTP `status`, CatMQ's `code` (such as `queue_not_found`, `queue_full` or `lease_expired`), a `title`, and `retryAfter` when CatMQ gives one. `status` is 0 when CatMQ could not be reached.

```js
import { CatMQError } from 'catmq';

try {
  await mq.publish('orders', { orderId: 1 });
} catch (error) {
  if (error instanceof CatMQError && error.code === 'queue_full') {
    // slow down
  }
}
```

## Webhooks

```js
const webhook = await mq.putWebhook('orders', 'billing', {
  url: 'https://billing.example.com/catmq',
  headers: { Authorization: 'Bearer ...' },
  maxConcurrency: 4,
});
const secret = webhook.secret; // returned only when the webhook is created; store it
```

In the receiver, check each delivery's signature against the raw body before trusting it:

```js
import express from 'express';
import { verifyWebhook, WebhookSignatureError } from 'catmq';

app.post('/catmq', express.raw({ type: '*/*' }), async (req, res) => {
  let delivery;
  try {
    delivery = await verifyWebhook(req.body, req.get('X-CatMQ-Signature'), secret);
  } catch (error) {
    if (error instanceof WebhookSignatureError) return res.sendStatus(401);
    throw error;
  }
  await process(delivery.body);
  res.sendStatus(200); // 410 drops it, 422 dead-letters it, 429/503 defer it (Retry-After), anything else retries
});
```

`mq.rotateWebhookSecret(queue, name)` returns a new secret; for 24 hours deliveries are signed with both, and `verifyWebhook` accepts an array of secrets while you switch.

## Everything else

| Area | Methods |
|---|---|
| Queues | `listQueues`, `iterQueues`, `createQueue`, `getQueue`, `updateQueue`, `deleteQueue`, `purgeQueue`, `redrive` |
| Messages | `publish`, `publishBatch`, `listMessages`, `iterMessages`, `getMessage`, `deleteMessage` |
| Leases | `receive`, `ack`, `ackBatch`, `extend`, `consume` |
| Consumers | `listConsumers`, `deleteConsumer` |
| Webhooks | `listWebhooks`, `putWebhook`, `getWebhook`, `deleteWebhook`, `rotateWebhookSecret`, `verifyWebhook` |
| System | `health` |

Results are the API's JSON. The full API is described in [docs/api/catmq.openapi.yaml](../../docs/api/catmq.openapi.yaml).

## Tests

`test/conformance.mjs` runs the shared scenarios in [clients/conformance](../conformance/scenarios.json) against a running service. The CatMQ test suite runs it against a real service as part of `dotnet test`.

```bash
CATMQ_URL=http://127.0.0.1:45783 CATMQ_API_KEY=... node test/conformance.mjs
```
