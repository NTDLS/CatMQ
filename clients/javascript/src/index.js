// A client for the CatMQ HTTP API. It has no dependencies, and runs on Node.js 18 or later, Deno, Bun and browsers.

const DISPOSITIONS = ['consumed', 'notConsumed', 'notInterested', 'deadLetter', 'drop'];

/**
 * An error response from CatMQ (a problem details document), or a failure to reach it.
 * status is the HTTP status (0 when CatMQ could not be reached), code is CatMQ's stable error code, such as
 * queue_not_found or queue_full, and retryAfter is the seconds to wait before retrying, when CatMQ gave one.
 */
export class CatMQError extends Error {
  constructor(status, code, title, detail = undefined, retryAfter = undefined, options = undefined) {
    super(status ? `${title} (${status} ${code})` : title, options);
    this.name = 'CatMQError';
    this.status = status;
    this.code = code;
    this.title = title;
    this.detail = detail;
    this.retryAfter = retryAfter;
  }
}

/** A webhook delivery whose signature is missing, wrong, or too old. */
export class WebhookSignatureError extends Error {
  constructor(message) {
    super(message);
    this.name = 'WebhookSignatureError';
  }
}

const segment = (value) => encodeURIComponent(String(value));

function toBase64(bytes) {
  if (typeof Buffer !== 'undefined') return Buffer.from(bytes).toString('base64');
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

function fromBase64(text) {
  if (typeof Buffer !== 'undefined') return new Uint8Array(Buffer.from(text, 'base64'));
  return Uint8Array.from(atob(text), (c) => c.charCodeAt(0));
}

function isBytes(value) {
  return value instanceof Uint8Array || value instanceof ArrayBuffer;
}

/** A message in the API's form. Bytes are sent as bodyBase64; anything else is sent as body. */
function toApiMessage(message) {
  const { body, contentType, messageType, headers, deferSeconds, idempotencyKey } = message;
  const result = {};
  if (isBytes(body)) {
    result.bodyBase64 = toBase64(body instanceof ArrayBuffer ? new Uint8Array(body) : body);
    result.contentType = contentType ?? 'application/octet-stream';
  } else {
    result.body = body;
    if (contentType !== undefined) result.contentType = contentType;
  }
  if (messageType !== undefined) result.messageType = messageType;
  if (headers !== undefined) result.headers = headers;
  if (deferSeconds !== undefined) result.deferSeconds = deferSeconds;
  if (idempotencyKey !== undefined) result.idempotencyKey = idempotencyKey;
  return result;
}

function toPublishResult(item) {
  return {
    status: item.status,
    messageId: item.messageId,
    error: item.error ? new CatMQError(item.error.status, item.error.code, item.error.title) : undefined,
  };
}

const sleep = (ms, signal) => new Promise((resolve) => {
  if (signal?.aborted) return resolve();
  const timer = setTimeout(done, ms);
  function done() {
    clearTimeout(timer);
    signal?.removeEventListener('abort', done);
    resolve();
  }
  signal?.addEventListener('abort', done, { once: true });
});

/**
 * A message received under a lease. Settle it with one of ack, nack, defer, deadLetter, drop or notInterested
 * before the lease expires, or extend the lease. Otherwise it is delivered again.
 */
export class ReceivedMessage {
  constructor(client, queue, data) {
    this.client = client;
    this.queue = queue;
    this.raw = data;
    this.messageId = data.messageId;
    this.serialNumber = data.serialNumber;
    this.messageType = data.messageType ?? null;
    this.contentType = data.contentType ?? 'application/json';
    this.headers = data.headers ?? {};
    this.enqueuedAt = data.enqueuedAt;
    this.deliveryAttempt = data.deliveryAttempt ?? 1;
    this.leaseId = data.leaseId;
    this.leaseExpiresAt = data.leaseExpiresAt;
    this.settled = false;
    // JSON bodies arrive parsed, other text as a string, and binary bodies as a Uint8Array.
    this.body = data.bodyBase64 !== undefined ? fromBase64(data.bodyBase64) : data.body;
  }

  async #settle(disposition, deferSeconds) {
    await this.client.ack(this.leaseId, disposition, { deferSeconds });
    this.settled = true;
  }

  /** The message was processed, so it is removed. */
  ack() { return this.#settle('consumed'); }

  /** The message was not processed. It is delivered again, which counts as a failed attempt. */
  nack() { return this.#settle('notConsumed'); }

  /** Deliver the message again after the given number of seconds, without counting a failed attempt. */
  defer(seconds) { return this.#settle('defer', seconds); }

  /** Move the message to the queue's dead-letter queue. */
  deadLetter() { return this.#settle('deadLetter'); }

  /** Remove the message without processing it. */
  drop() { return this.#settle('drop'); }

  /** This consumer does not want the message; it goes to another consumer. */
  notInterested() { return this.#settle('notInterested'); }

  /** Extend the lease to the given number of seconds from now. Returns the new expiry. */
  async extend(seconds) {
    this.leaseExpiresAt = await this.client.extend(this.leaseId, seconds);
    return this.leaseExpiresAt;
  }
}

/**
 * A client for the CatMQ HTTP API.
 *
 *   const mq = new CatMQ({ url: 'http://localhost:45783', apiKey: process.env.CATMQ_API_KEY });
 */
export class CatMQ {
  #base;
  #apiKey;
  #timeoutMs;
  #fetch;

  /**
   * @param {object} options
   * @param {string} options.url The service's base URL, such as http://localhost:45783.
   * @param {string} [options.apiKey]
   * @param {number} [options.timeoutMs=30000] A receive that waits for messages allows for its wait on top of it.
   * @param {typeof fetch} [options.fetch] A fetch implementation, if not the global one.
   */
  constructor({ url, apiKey, timeoutMs = 30000, fetch: fetchImpl } = {}) {
    if (!/^https?:\/\//i.test(url ?? '')) throw new TypeError(`Not an http or https URL: ${url}`);
    this.#base = url.replace(/\/+$/, '') + '/api/v1';
    this.#apiKey = apiKey;
    this.#timeoutMs = timeoutMs;
    this.#fetch = fetchImpl ?? globalThis.fetch.bind(globalThis);
  }

  async #request(method, path, body, { query, extraTimeoutMs = 0, authenticate = true, signal } = {}) {
    let url = this.#base + path;
    if (query) {
      const params = new URLSearchParams();
      for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== null) params.set(key, String(value));
      const text = params.toString();
      if (text) url += '?' + text;
    }

    const headers = { Accept: 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (authenticate && this.#apiKey) headers.Authorization = `Bearer ${this.#apiKey}`;

    const timeout = AbortSignal.timeout(this.#timeoutMs + extraTimeoutMs);
    const signals = signal ? [timeout, signal] : [timeout];

    let response;
    try {
      response = await this.#fetch(url, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: signals.length === 1 ? timeout : AbortSignal.any(signals),
      });
    } catch (error) {
      if (signal?.aborted) throw error;
      throw new CatMQError(0, 'connection_failed', `Could not reach CatMQ: ${error.message}`, undefined, undefined, { cause: error });
    }

    const text = await response.text();
    if (!response.ok) {
      let problem = {};
      try { problem = text ? JSON.parse(text) : {}; } catch { /* Not JSON. */ }
      if (typeof problem !== 'object' || problem === null) problem = {};
      const retryAfterHeader = response.headers.get('Retry-After');
      const retryAfter = retryAfterHeader && /^\d+$/.test(retryAfterHeader.trim()) ? Number(retryAfterHeader) : undefined;
      throw new CatMQError(response.status, problem.code ?? `http_${response.status}`,
        problem.title ?? (response.statusText || 'Request failed.'), problem.detail, retryAfter);
    }
    return text ? JSON.parse(text) : undefined;
  }

  // ---- System ----

  /** The service's status and version. This needs no API key. */
  health() {
    return this.#request('GET', '/health', undefined, { authenticate: false });
  }

  // ---- Queues ----

  /** One page of queues: { queues, nextCursor }. */
  listQueues({ limit, cursor } = {}) {
    return this.#request('GET', '/queues', undefined, { query: { limit, cursor } });
  }

  /** Every queue, fetching pages as needed. */
  async *iterQueues({ pageSize = 100 } = {}) {
    let cursor;
    do {
      const page = await this.listQueues({ limit: pageSize, cursor });
      yield* page.queues;
      cursor = page.nextCursor;
    } while (cursor);
  }

  /**
   * Creates a queue, or replaces the settings of an existing one; settings left out return to their defaults:
   * persistence, consumptionScheme, deliveryScheme, maxOutstandingDeliveries, maxDeliveryAttempts,
   * maxMessageAgeSeconds, deliveryThrottleMilliseconds, maxDepth and deadLetter.
   */
  createQueue(name, settings = {}) {
    return this.#request('PUT', `/queues/${segment(name)}`, settings);
  }

  /** A queue's settings and stats. */
  getQueue(name) {
    return this.#request('GET', `/queues/${segment(name)}`);
  }

  /** Changes only the settings given. null removes a limit (maxMessageAgeSeconds, maxDepth). */
  updateQueue(name, changes) {
    return this.#request('PATCH', `/queues/${segment(name)}`, changes);
  }

  async deleteQueue(name) {
    await this.#request('DELETE', `/queues/${segment(name)}`);
  }

  /** Removes every message from a queue. */
  async purgeQueue(name) {
    await this.#request('DELETE', `/queues/${segment(name)}/messages`);
  }

  /** Moves messages from a queue's dead-letter queue back to it. Returns how many were moved. */
  async redrive(name, { max } = {}) {
    return (await this.#request('POST', `/queues/${segment(name)}/redrive`, max === undefined ? {} : { max })).moved;
  }

  // ---- Publishing ----

  /**
   * Publishes one message. An object, array, number or other JSON value is sent as JSON; a string with a text
   * contentType is sent as text; a Uint8Array or ArrayBuffer is sent as binary. Throws a CatMQError with code
   * queue_full (429) when the queue is at its maximum depth.
   * @returns {Promise<{status: string, messageId?: string, error?: CatMQError}>}
   */
  async publish(queue, body, options = {}) {
    const result = await this.#request('POST', `/queues/${segment(queue)}/messages`, toApiMessage({ ...options, body }));
    return toPublishResult(result.results[0]);
  }

  /**
   * Publishes up to 1000 messages in one request. Each is { body, contentType?, messageType?, headers?,
   * deferSeconds?, idempotencyKey? }. Returns a result per message, in order; an invalid message is rejected
   * (with its error) without failing the others.
   */
  async publishBatch(queue, messages) {
    const result = await this.#request('POST', `/queues/${segment(queue)}/messages`, { messages: messages.map(toApiMessage) });
    return result.results.map(toPublishResult);
  }

  // ---- Browsing ----

  /** One page of the messages waiting in a queue, oldest first, without leasing them: { messages, nextCursor }. */
  listMessages(queue, { limit, cursor, includeBody } = {}) {
    return this.#request('GET', `/queues/${segment(queue)}/messages`, undefined,
      { query: { limit, cursor, includeBody: includeBody ? 'true' : undefined } });
  }

  async *iterMessages(queue, { pageSize = 100, includeBody = false } = {}) {
    let cursor;
    do {
      const page = await this.listMessages(queue, { limit: pageSize, cursor, includeBody });
      yield* page.messages;
      cursor = page.nextCursor;
    } while (cursor);
  }

  getMessage(queue, serialNumber) {
    return this.#request('GET', `/queues/${segment(queue)}/messages/${segment(serialNumber)}`);
  }

  async deleteMessage(queue, serialNumber) {
    await this.#request('DELETE', `/queues/${segment(queue)}/messages/${segment(serialNumber)}`);
  }

  // ---- Receiving ----

  /**
   * Leases up to max messages for the named consumer, waiting up to waitSeconds (at most 60) for one to arrive.
   * Returns an empty array if none did. Consumers with the same name share the messages.
   * @returns {Promise<ReceivedMessage[]>}
   */
  async receive(queue, consumer, { max = 1, waitSeconds = 20, leaseSeconds = 60, signal } = {}) {
    const result = await this.#request('POST', `/queues/${segment(queue)}/messages/receive`,
      { consumer, max, waitSeconds, leaseSeconds }, { extraTimeoutMs: waitSeconds * 1000, signal });
    return result.messages.map((item) => new ReceivedMessage(this, queue, item));
  }

  /**
   * Settles a lease. disposition is consumed, notConsumed, notInterested, deadLetter, drop or defer (with
   * deferSeconds). Throws lease_not_found (404) or lease_expired (409).
   */
  async ack(leaseId, disposition = 'consumed', { deferSeconds } = {}) {
    const body = { disposition };
    if (deferSeconds !== undefined) body.deferSeconds = deferSeconds;
    await this.#request('POST', `/leases/${segment(leaseId)}/ack`, body);
  }

  /**
   * Settles up to 1000 leases at once. Each is a lease ID (consumed) or { leaseId, disposition, deferSeconds }.
   * Returns { leaseId, status } for each, where status is acked, notFound or leaseExpired.
   */
  async ackBatch(acks) {
    const items = acks.map((ack) => (typeof ack === 'string'
      ? { leaseId: ack, disposition: 'consumed' }
      : { disposition: 'consumed', ...ack }));
    return (await this.#request('POST', '/leases/ack', { acks: items })).results;
  }

  /** Extends a lease to the given number of seconds from now. Returns the new expiry. */
  async extend(leaseId, seconds) {
    return (await this.#request('POST', `/leases/${segment(leaseId)}/extend`, { leaseSeconds: seconds })).leaseExpiresAt;
  }

  /**
   * Receives and handles messages until the signal is aborted, or, with untilIdle, until a receive finds none.
   *
   * The handler is called with each message, one at a time. When it resolves, the message is acknowledged as
   * consumed, or with the disposition it returns (such as 'drop' or 'deadLetter'). If it throws, the message is
   * delivered again (notConsumed). A handler can also settle the message itself, such as with message.defer(30).
   * Connection failures and server errors are retried with a growing delay. Resolves to how many messages were handled.
   */
  async consume(queue, consumer, handler, { max = 10, waitSeconds = 20, leaseSeconds = 60, signal, untilIdle = false, onError } = {}) {
    let handled = 0;
    let backoffMs = 1000;
    const report = onError ?? ((error, message) => console.error(
      message ? `CatMQ: the handler failed for message ${message.messageId}; it will be delivered again.` : 'CatMQ: receive failed.', error));

    while (!signal?.aborted) {
      let messages;
      try {
        messages = await this.receive(queue, consumer, { max, waitSeconds, leaseSeconds, signal });
      } catch (error) {
        if (signal?.aborted) break;
        if (!(error instanceof CatMQError) || (error.status !== 0 && error.status < 500 && error.status !== 429)) throw error;
        report(error);
        await sleep(error.retryAfter ? error.retryAfter * 1000 : backoffMs, signal);
        backoffMs = Math.min(backoffMs * 2, 30000);
        continue;
      }
      backoffMs = 1000;

      if (messages.length === 0) {
        if (untilIdle) break;
        continue;
      }

      const acks = [];
      for (const message of messages) {
        let disposition;
        try {
          disposition = await handler(message);
        } catch (error) {
          report(error, message);
          disposition = 'notConsumed';
        }
        handled++;
        if (message.settled) continue;
        if (disposition === undefined || disposition === null || disposition === true) disposition = 'consumed';
        if (!DISPOSITIONS.includes(disposition)) {
          throw new TypeError(`The handler returned ${JSON.stringify(disposition)}; return a disposition, or call message.defer(seconds).`);
        }
        acks.push({ leaseId: message.leaseId, disposition });
      }

      if (acks.length > 0) {
        for (const result of await this.ackBatch(acks)) {
          if (result.status !== 'acked') {
            report(new Error(`Lease ${result.leaseId} was ${result.status} when acknowledged; the message may be delivered again.`));
          }
        }
      }
    }
    return handled;
  }

  // ---- Consumers ----

  /** The queue's consumers: { name, kind, connections }, where kind is tcp, lease or webhook. */
  async listConsumers(queue) {
    return (await this.#request('GET', `/queues/${segment(queue)}/consumers`)).consumers;
  }

  async deleteConsumer(queue, name) {
    await this.#request('DELETE', `/queues/${segment(queue)}/consumers/${segment(name)}`);
  }

  // ---- Webhooks ----

  async listWebhooks(queue) {
    return (await this.#request('GET', `/queues/${segment(queue)}/webhooks`)).webhooks;
  }

  /**
   * Creates a webhook, or replaces its settings ({ url, timeoutSeconds?, maxConcurrency?, headers?, enabled? }).
   * When it is created, the result includes its signing secret, which is never returned again; keep it to verify
   * deliveries with verifyWebhook.
   */
  putWebhook(queue, name, settings) {
    return this.#request('PUT', `/queues/${segment(queue)}/webhooks/${segment(name)}`, settings);
  }

  getWebhook(queue, name) {
    return this.#request('GET', `/queues/${segment(queue)}/webhooks/${segment(name)}`);
  }

  async deleteWebhook(queue, name) {
    await this.#request('DELETE', `/queues/${segment(queue)}/webhooks/${segment(name)}`);
  }

  /** Replaces the signing secret. For the next 24 hours deliveries are signed with both, so receivers can switch. */
  async rotateWebhookSecret(queue, name) {
    return (await this.#request('POST', `/queues/${segment(queue)}/webhooks/${segment(name)}/rotate-secret`)).secret;
  }
}

const encoder = new TextEncoder();

async function hmacHex(secret, data) {
  const subtle = globalThis.crypto?.subtle ?? (await import('node:crypto')).webcrypto.subtle;
  const key = await subtle.importKey('raw', encoder.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const signature = new Uint8Array(await subtle.sign('HMAC', key, data));
  return Array.from(signature, (byte) => byte.toString(16).padStart(2, '0')).join('');
}

function constantTimeEqual(a, b) {
  if (a.length !== b.length) return false;
  let difference = 0;
  for (let i = 0; i < a.length; i++) difference |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return difference === 0;
}

/**
 * Checks a webhook delivery's X-CatMQ-Signature header against the raw request body, and returns the delivery.
 *
 * Pass the body exactly as it was received (a string, Uint8Array or ArrayBuffer), before any parsing. secret may be an
 * array, to accept either secret while moving to a new one. Deliveries signed more than toleranceSeconds ago are
 * rejected, so that a captured delivery cannot be replayed later. Throws a WebhookSignatureError if it is not genuine.
 */
export async function verifyWebhook(body, signature, secret, { toleranceSeconds = 300, now } = {}) {
  const raw = typeof body === 'string' ? encoder.encode(body) : new Uint8Array(body);
  if (!signature) throw new WebhookSignatureError('The delivery has no X-CatMQ-Signature header.');

  let timestamp;
  const signatures = [];
  for (const part of signature.split(',')) {
    const index = part.indexOf('=');
    const key = part.slice(0, index).trim();
    const value = part.slice(index + 1).trim();
    if (key === 't') timestamp = value;
    else if (key === 'v1') signatures.push(value);
  }

  if (!timestamp || !/^\d+$/.test(timestamp) || signatures.length === 0) {
    throw new WebhookSignatureError('The X-CatMQ-Signature header is not valid.');
  }

  const current = now ?? Date.now() / 1000;
  if (Math.abs(current - Number(timestamp)) > toleranceSeconds) {
    throw new WebhookSignatureError("The delivery's timestamp is outside the tolerance.");
  }

  const prefix = encoder.encode(timestamp + '.');
  const signed = new Uint8Array(prefix.length + raw.length);
  signed.set(prefix);
  signed.set(raw, prefix.length);

  for (const candidate of Array.isArray(secret) ? secret : [secret]) {
    const expected = await hmacHex(candidate, signed);
    if (signatures.some((given) => constantTimeEqual(expected, given))) {
      return JSON.parse(new TextDecoder().decode(raw));
    }
  }
  throw new WebhookSignatureError("The delivery's signature does not match the secret.");
}

export default CatMQ;
