// Runs the shared conformance scenarios (clients/conformance/scenarios.json) against a running CatMQ service.
//
//   CATMQ_URL=http://127.0.0.1:45783 CATMQ_API_KEY=... node test/conformance.mjs
//
// Prints one JSON line per scenario, and exits with 1 if any failed or is not implemented.

import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { CatMQ, CatMQError, WebhookSignatureError, verifyWebhook } from '../src/index.js';

const URL_ = process.env.CATMQ_URL ?? 'http://127.0.0.1:45783';
const API_KEY = process.env.CATMQ_API_KEY;
const WEBHOOK_HOST = process.env.CATMQ_WEBHOOK_HOST ?? '127.0.0.1';
const SCENARIOS = new URL('../../conformance/scenarios.json', import.meta.url);

const mq = new CatMQ({ url: URL_, apiKey: API_KEY });
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function newQueue(settings = {}) {
  const name = 'js' + randomUUID().replaceAll('-', '');
  await mq.createQueue(name, settings);
  return name;
}

const depth = async (queue) => (await mq.getQueue(queue)).stats.depth;

async function waitUntil(condition, seconds = 10) {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    if (await condition()) return true;
    await sleep(50);
  }
  return condition();
}

async function expectError(status, code, action) {
  try {
    await action();
  } catch (error) {
    assert.ok(error instanceof CatMQError, `Expected a CatMQError, got ${error}`);
    assert.equal(error.status, status);
    assert.equal(error.code, code);
    assert.ok(error.title, 'The error has no title.');
    return error;
  }
  assert.fail(`Expected ${status} ${code}, but nothing was thrown.`);
}

async function receiveAll(queue, consumer, count, options = {}) {
  const received = [];
  const deadline = Date.now() + 10000;
  while (received.length < count && Date.now() < deadline) {
    received.push(...await mq.receive(queue, consumer, { max: count - received.length, waitSeconds: 2, ...options }));
  }
  assert.equal(received.length, count, `Received ${received.length} of ${count} messages.`);
  return received;
}

const scenarios = {
  async health() {
    const result = await new CatMQ({ url: URL_ }).health();
    assert.equal(result.status, 'ok');
    assert.ok(result.version);
  },

  async unauthorized() {
    await expectError(401, 'unauthorized', () => new CatMQ({ url: URL_, apiKey: 'not-a-real-key' }).listQueues());
  },

  async queue_lifecycle() {
    const name = 'js' + randomUUID().replaceAll('-', '');
    const created = await mq.createQueue(name, { consumptionScheme: 'firstConsumer', maxOutstandingDeliveries: 3 });
    assert.equal(created.name, name);
    assert.equal(created.settings.consumptionScheme, 'firstConsumer');
    assert.equal(created.settings.maxOutstandingDeliveries, 3);

    const again = await mq.createQueue(name, { consumptionScheme: 'firstConsumer', maxOutstandingDeliveries: 4 });
    assert.equal(again.settings.maxOutstandingDeliveries, 4);

    const updated = await mq.updateQueue(name, { maxDepth: 50 });
    assert.equal(updated.settings.maxDepth, 50);
    assert.equal(updated.settings.maxOutstandingDeliveries, 4);
    assert.equal((await mq.updateQueue(name, { maxDepth: null })).settings.maxDepth, null);
    assert.equal(await depth(name), 0);

    assert.equal((await mq.listQueues({ limit: 1 })).queues.length, 1);
    let found = false;
    for await (const queue of mq.iterQueues({ pageSize: 2 })) found ||= queue.name === name;
    assert.ok(found, 'The queue was not found while paging.');

    await mq.deleteQueue(name);
    await expectError(404, 'queue_not_found', () => mq.getQueue(name));
  },

  async invalid_request() {
    await expectError(400, 'invalid_request', () => mq.createQueue('js' + randomUUID().replaceAll('-', ''), { persistence: 'sometimes' }));
    const queue = await newQueue();
    await expectError(400, 'invalid_request', () => mq.receive(queue, 'bad name', { waitSeconds: 0 }));
  },

  async publish_receive_ack() {
    const queue = await newQueue({ consumptionScheme: 'firstConsumer' });
    const first = await mq.publish(queue, { orderId: 7, items: ['a', 'b'] }, { messageType: 'orders.created', headers: { tenant: 'acme' } });
    assert.equal(first.status, 'accepted');
    assert.ok(first.messageId);
    await mq.publish(queue, 'hello there', { contentType: 'text/plain' });
    await mq.publish(queue, new Uint8Array([0, 1, 2, 255]));

    const messages = (await receiveAll(queue, 'worker', 3)).sort((a, b) => Number(BigInt(a.serialNumber) - BigInt(b.serialNumber)));
    assert.equal(messages[0].messageId, first.messageId);
    assert.deepEqual(messages[0].body, { orderId: 7, items: ['a', 'b'] });
    assert.equal(messages[0].messageType, 'orders.created');
    assert.deepEqual(messages[0].headers, { tenant: 'acme' });
    assert.equal(messages[0].deliveryAttempt, 1);
    assert.ok(messages[0].leaseId);
    assert.equal(messages[1].body, 'hello there');
    assert.ok(messages[1].contentType.startsWith('text/plain'));
    assert.deepEqual(messages[2].body, new Uint8Array([0, 1, 2, 255]));
    assert.equal(messages[2].contentType, 'application/octet-stream');

    for (const message of messages) await message.ack();
    assert.ok(await waitUntil(async () => await depth(queue) === 0));
  },

  async batch_publish() {
    const queue = await newQueue();
    const results = await mq.publishBatch(queue, [
      { body: { n: 1 } },
      { body: '', contentType: 'text/plain' },
      { body: { n: 3 }, messageType: 'third' },
    ]);
    assert.deepEqual(results.map((r) => r.status), ['accepted', 'rejected', 'accepted']);
    assert.equal(results[1].error?.code, 'invalid_request');
    assert.ok(results[0].messageId && results[2].messageId);
    assert.ok(await waitUntil(async () => await depth(queue) === 2));
  },

  async idempotency() {
    const queue = await newQueue();
    const idempotencyKey = randomUUID();
    const first = await mq.publish(queue, { n: 1 }, { idempotencyKey });
    const second = await mq.publish(queue, { n: 1 }, { idempotencyKey });
    assert.equal(first.status, 'accepted');
    assert.equal(second.status, 'duplicate');
    assert.equal(first.messageId, second.messageId);
    assert.equal(await depth(queue), 1);
  },

  async queue_full() {
    const queue = await newQueue({ maxDepth: 1 });
    await mq.publish(queue, { n: 1 });
    await expectError(429, 'queue_full', () => mq.publish(queue, { n: 2 }));
  },

  async lease_dispositions() {
    const queue = await newQueue({ consumptionScheme: 'firstConsumer' });
    await mq.publish(queue, { n: 1 });

    const [message] = await receiveAll(queue, 'worker', 1, { leaseSeconds: 30 });
    const before = message.leaseExpiresAt;
    const expires = await message.extend(600);
    assert.ok(expires && expires !== before);

    await message.nack();
    const [again] = await receiveAll(queue, 'worker', 1);
    assert.equal(again.messageId, message.messageId);
    assert.equal(again.deliveryAttempt, 2);

    await again.defer(2);
    assert.deepEqual(await mq.receive(queue, 'worker', { waitSeconds: 1 }), []);
    const [later] = await receiveAll(queue, 'worker', 1);
    assert.equal(later.messageId, message.messageId);
    await later.ack();

    await expectError(404, 'lease_not_found', () => mq.ack('no-such-lease'));
  },

  async batch_ack() {
    const queue = await newQueue({ consumptionScheme: 'firstConsumer' });
    await mq.publishBatch(queue, [0, 1, 2].map((n) => ({ body: { n } })));
    const messages = await receiveAll(queue, 'worker', 3);

    const results = await mq.ackBatch([messages[0].leaseId, { leaseId: messages[1].leaseId, disposition: 'drop' },
      { leaseId: messages[2].leaseId, disposition: 'consumed' }, 'no-such-lease']);
    assert.deepEqual(results.map((r) => r.status), ['acked', 'acked', 'acked', 'notFound']);
    assert.ok(await waitUntil(async () => await depth(queue) === 0));
  },

  async consume_loop() {
    const queue = await newQueue({ consumptionScheme: 'firstConsumer', deadLetter: {} });
    await mq.publishBatch(queue, [0, 1, 2, 3, 4].map((n) => ({ body: { n } })));

    const attempts = new Map();
    const handled = await mq.consume(queue, 'loop', async (message) => {
      const { n } = message.body;
      attempts.set(n, (attempts.get(n) ?? 0) + 1);
      if (n === 1 && attempts.get(n) === 1) throw new Error('A failure the loop should recover from.');
      if (n === 2) return 'deadLetter';
      return undefined;
    }, { max: 5, waitSeconds: 1, untilIdle: true, onError: () => {} });

    assert.equal(handled, 6, `Handled ${handled} times.`);
    assert.equal(attempts.get(1), 2);
    assert.ok(await waitUntil(async () => await depth(queue) === 0));
    assert.ok(await waitUntil(async () => await depth(`${queue}.dlq`) === 1));
  },

  async dead_letter_redrive() {
    const queue = await newQueue({ consumptionScheme: 'firstConsumer', deadLetter: { persistence: 'ephemeral' } });
    await mq.publish(queue, { n: 1 });
    const [message] = await receiveAll(queue, 'worker', 1);
    await message.deadLetter();
    assert.ok(await waitUntil(async () => await depth(`${queue}.dlq`) === 1 && await depth(queue) === 0));
    assert.equal((await mq.getQueue(`${queue}.dlq`)).isDeadLetter, true);

    assert.equal(await mq.redrive(queue), 1);
    assert.ok(await waitUntil(async () => await depth(queue) === 1 && await depth(`${queue}.dlq`) === 0));
  },

  async browse_messages() {
    const queue = await newQueue();
    await mq.publishBatch(queue, [0, 1, 2, 3, 4].map((n) => ({ body: { n } })));

    const first = await mq.listMessages(queue, { limit: 2 });
    assert.equal(first.messages.length, 2);
    assert.ok(first.nextCursor);
    assert.equal(first.messages[0].body, undefined);
    const all = [];
    for await (const message of mq.iterMessages(queue, { pageSize: 2, includeBody: true })) all.push(message);
    assert.deepEqual(all.map((m) => m.body.n), [0, 1, 2, 3, 4]);

    const serial = all[2].serialNumber;
    assert.deepEqual((await mq.getMessage(queue, serial)).body, { n: 2 });
    await mq.deleteMessage(queue, serial);
    await expectError(404, 'message_not_found', () => mq.getMessage(queue, serial));
    assert.equal(await depth(queue), 4);

    await mq.purgeQueue(queue);
    assert.equal(await depth(queue), 0);
  },

  async consumers() {
    const queue = await newQueue();
    assert.deepEqual(await mq.receive(queue, 'reporting', { waitSeconds: 0 }), []);
    const listed = await mq.listConsumers(queue);
    assert.deepEqual(listed.map((c) => [c.name, c.kind]), [['reporting', 'lease']]);
    await mq.deleteConsumer(queue, 'reporting');
    assert.deepEqual(await mq.listConsumers(queue), []);
  },

  async webhooks() {
    const deliveries = [];
    const server = createServer((request, response) => {
      const chunks = [];
      request.on('data', (chunk) => chunks.push(chunk));
      request.on('end', () => {
        deliveries.push({ body: Buffer.concat(chunks), signature: request.headers['x-catmq-signature'] });
        response.writeHead(200).end();
      });
    });
    await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));

    try {
      const queue = await newQueue();
      const url = `http://${WEBHOOK_HOST}:${server.address().port}/hook`;

      const created = await mq.putWebhook(queue, 'orders', { url, timeoutSeconds: 10, headers: { Authorization: 'Bearer token' } });
      const { secret } = created;
      assert.ok(secret.startsWith('whsec_'));
      assert.deepEqual(created.headers, { Authorization: '********' });
      assert.equal((await mq.getWebhook(queue, 'orders')).secret, undefined);
      assert.deepEqual((await mq.listWebhooks(queue)).map((w) => w.name), ['orders']);

      await mq.publish(queue, { orderId: 9 }, { messageType: 'orders.created' });
      assert.ok(await waitUntil(() => deliveries.length === 1));
      const { body, signature } = deliveries[0];

      const delivery = await verifyWebhook(body, signature, secret);
      assert.equal(delivery.queue, queue);
      assert.deepEqual(delivery.body, { orderId: 9 });
      assert.equal(delivery.messageType, 'orders.created');
      assert.equal(delivery.deliveryAttempt, 1);

      for (const bad of [
        () => verifyWebhook(body, signature, 'whsec_wrong'),
        () => verifyWebhook(body, signature, secret, { now: Date.now() / 1000 + 3600 }),
        () => verifyWebhook(Buffer.concat([body, Buffer.from(' ')]), signature, secret),
        () => verifyWebhook(body, undefined, secret),
      ]) {
        await assert.rejects(bad, WebhookSignatureError);
      }

      const newSecret = await mq.rotateWebhookSecret(queue, 'orders');
      assert.ok(newSecret.startsWith('whsec_') && newSecret !== secret);
      await mq.publish(queue, { orderId: 10 });
      assert.ok(await waitUntil(() => deliveries.length === 2));
      await verifyWebhook(deliveries[1].body, deliveries[1].signature, newSecret);
      await verifyWebhook(deliveries[1].body, deliveries[1].signature, [secret]);

      await mq.deleteWebhook(queue, 'orders');
      await expectError(404, 'webhook_not_found', () => mq.getWebhook(queue, 'orders'));
    } finally {
      server.close();
    }
  },
};

const ids = JSON.parse(readFileSync(SCENARIOS, 'utf8')).scenarios.map((s) => s.id);
let failed = false;
for (const id of ids) {
  const result = { scenario: id, passed: false };
  if (!scenarios[id]) {
    result.error = 'Not implemented.';
  } else {
    try {
      await scenarios[id]();
      result.passed = true;
    } catch (error) {
      result.error = error?.stack ?? String(error);
    }
  }
  failed ||= !result.passed;
  console.log(JSON.stringify(result));
}
process.exit(failed ? 1 : 0);
