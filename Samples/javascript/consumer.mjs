// Consumes the sample messages with the JavaScript client.
//
//   node consumer.mjs [--until-idle] [--manual]
//
// By default it uses the client's consume loop. With --manual it receives and acknowledges each message itself.
// Outside this repository, install the client and import from 'catmq' instead.
import { CatMQ } from '../../clients/javascript/src/index.js';

const QUEUE = 'samples.orders';
const CONSUMER = 'javascript-worker'; // Consumers that share a name compete for messages, like several workers of one service.

const mq = new CatMQ({ url: process.env.CATMQ_URL ?? 'http://127.0.0.1:45783', apiKey: process.env.CATMQ_API_KEY });
await mq.createQueue(QUEUE, {
  persistence: 'persistent',
  consumptionScheme: 'firstConsumer',
  maxOutstandingDeliveries: 10,
  deadLetter: { persistence: 'persistent' },
});

const untilIdle = process.argv.includes('--until-idle');
const waitSeconds = untilIdle ? 7 : 20;

// Returns a disposition. Returning nothing acknowledges the message; throwing has it delivered again.
async function handle(message) {
  const source = message.headers.source ?? 'unknown';

  switch (message.messageType) {
    case 'orders.created': {
      const order = message.body; // JSON arrives parsed.
      if (!order.customer) {
        console.log(`Order ${order.orderId} from ${source} has no customer: dead-lettering it.`);
        return 'deadLetter';
      }
      console.log(`Order ${order.orderId} for ${order.customer}, ${order.total.toFixed(2)}, from ${source} (attempt ${message.deliveryAttempt}).`);
      return undefined;
    }
    case 'orders.note':
      console.log(`Note from ${source}: ${message.body}`); // Text arrives as a string.
      return undefined;
    case 'orders.receipt': {
      const bytes = message.body; // Binary arrives as a Uint8Array.
      console.log(`Receipt from ${source}: ${bytes.length} bytes of ${message.contentType}, starting "${new TextDecoder().decode(bytes.slice(0, 8))}".`);
      return undefined;
    }
    default:
      return 'notInterested'; // Not for this consumer. Another one may want it.
  }
}

const stop = new AbortController();
process.on('SIGINT', () => stop.abort());
console.log(untilIdle ? `Consuming ${QUEUE} until it is idle...` : `Consuming ${QUEUE}. Press Ctrl+C to stop.`);

if (process.argv.includes('--manual')) {
  // Receive leases up to max messages for leaseSeconds. A message that is not settled before its lease ends is
  // delivered again; message.extend(seconds) keeps it longer.
  while (!stop.signal.aborted) {
    let messages;
    try {
      messages = await mq.receive(QUEUE, CONSUMER, { max: 10, waitSeconds, leaseSeconds: 60, signal: stop.signal });
    } catch (error) {
      if (stop.signal.aborted) break;
      throw error;
    }
    if (messages.length === 0 && untilIdle) break;

    for (const message of messages) {
      let disposition;
      try {
        disposition = await handle(message);
      } catch {
        await message.defer(30); // Try again in 30 seconds, without counting a failed attempt.
        continue;
      }
      if (disposition === undefined) await message.ack();
      else await mq.ack(message.leaseId, disposition);
    }
  }
} else {
  await mq.consume(QUEUE, CONSUMER, handle, { max: 10, waitSeconds, untilIdle, signal: stop.signal });
}
