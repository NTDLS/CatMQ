// Publishes the sample messages with the JavaScript client.
//
//   node publisher.mjs [count]
//
// Outside this repository, install the client and import from 'catmq' instead.
import { CatMQ } from '../../clients/javascript/src/index.js';

const QUEUE = 'samples.orders';

const mq = new CatMQ({ url: process.env.CATMQ_URL ?? 'http://127.0.0.1:45783', apiKey: process.env.CATMQ_API_KEY });

// Creates the queue, or applies these settings to it. Every sample uses the same ones.
await mq.createQueue(QUEUE, {
  persistence: 'persistent',
  consumptionScheme: 'firstConsumer',
  maxOutstandingDeliveries: 10,
  deadLetter: { persistence: 'persistent' },
});

const count = Number(process.argv[2] ?? 5);
const headers = { source: 'javascript' };

// One message per request. An object is sent as JSON.
await mq.publish(QUEUE, { orderId: 1, customer: 'Ada', total: 10.5 }, { messageType: 'orders.created', headers });

// Up to 1,000 per request. The idempotency keys make the request safe to retry: a key seen in the last
// 24 hours is reported as a duplicate, not stored again.
const orders = Array.from({ length: Math.max(0, count - 1) }, (_, index) => index + 2).map((i) => ({
  body: { orderId: i, customer: 'Ada', total: 10.5 * i },
  messageType: 'orders.created',
  headers,
  idempotencyKey: `javascript-order-${i}`,
}));

const results = await mq.publishBatch(QUEUE, [
  ...orders,
  { body: { orderId: 999, total: 1 }, messageType: 'orders.created', headers }, // No customer: dead-lettered.
  { body: 'Order 1 is a gift; please wrap it.', contentType: 'text/plain', messageType: 'orders.note', headers },
  { body: new TextEncoder().encode('%PDF-1.7 receipt'), messageType: 'orders.receipt', headers }, // Bytes are sent as binary.
  { body: 'Sent with a 5 second delay.', contentType: 'text/plain', messageType: 'orders.note', headers, deferSeconds: 5 },
]);

const accepted = 1 + results.filter((r) => r.status === 'accepted').length;
const duplicates = results.filter((r) => r.status === 'duplicate').length;
console.log(`Published ${accepted} messages to ${QUEUE}` + (duplicates ? ` (${duplicates} were duplicates of earlier runs).` : '.'));
