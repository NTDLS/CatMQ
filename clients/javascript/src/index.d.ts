export type Disposition = 'consumed' | 'notConsumed' | 'notInterested' | 'deadLetter' | 'drop';

export interface DeadLetterSettings {
  persistence?: 'ephemeral' | 'persistent';
  maxMessageAgeSeconds?: number | null;
}

export interface QueueSettings {
  persistence?: 'ephemeral' | 'persistent';
  consumptionScheme?: 'allConsumers' | 'firstConsumer';
  deliveryScheme?: 'random' | 'balanced';
  maxOutstandingDeliveries?: number;
  maxDeliveryAttempts?: number;
  maxMessageAgeSeconds?: number | null;
  deliveryThrottleMilliseconds?: number;
  maxDepth?: number | null;
  deadLetter?: DeadLetterSettings | null;
}

export interface QueueSettingsUpdate {
  maxOutstandingDeliveries?: number;
  maxDeliveryAttempts?: number;
  maxMessageAgeSeconds?: number | null;
  deliveryThrottleMilliseconds?: number;
  maxDepth?: number | null;
}

export interface QueueStats {
  depth: number;
  outstandingDeliveries: number;
  consumerCount: number;
  /** Counts are strings, because they can exceed what a JavaScript number holds exactly. */
  receivedCount: string;
  deliveredCount: string;
  failedCount: string;
  deferredCount: string;
  expiredCount: string;
  droppedCount: string;
  deadLetteredCount: string;
}

export interface Queue {
  name: string;
  isDeadLetter: boolean;
  settings: QueueSettings;
  stats: QueueStats;
}

export interface Message {
  messageId: string;
  serialNumber: string;
  messageType: string | null;
  contentType: string;
  body?: unknown;
  bodyBase64?: string;
  headers?: Record<string, string>;
  enqueuedAt: string;
  deferredUntil: string | null;
}

export interface PublishOptions {
  contentType?: string;
  messageType?: string;
  headers?: Record<string, string>;
  deferSeconds?: number;
  idempotencyKey?: string;
}

export interface OutgoingMessage extends PublishOptions {
  body: unknown;
}

export interface PublishResult {
  status: 'accepted' | 'duplicate' | 'rejected';
  messageId?: string;
  error?: CatMQError;
}

export interface Consumer {
  name: string;
  kind: 'tcp' | 'lease' | 'webhook';
  connections: number;
}

export interface WebhookSettings {
  url: string;
  timeoutSeconds?: number;
  maxConcurrency?: number | null;
  headers?: Record<string, string> | null;
  enabled?: boolean;
}

export interface Webhook {
  name: string;
  queue: string;
  url: string;
  timeoutSeconds: number;
  maxConcurrency: number | null;
  /** Header names; the values are never returned. */
  headers: Record<string, string> | null;
  enabled: boolean;
  lastSuccessAt: string | null;
  lastFailureAt: string | null;
  lastFailure: string | null;
  consecutiveFailures: number;
  /** Only when the webhook is created. */
  secret?: string;
}

export interface WebhookDelivery {
  queue: string;
  deliveryId: string;
  messageId: string;
  serialNumber: string;
  messageType: string | null;
  contentType: string;
  body?: unknown;
  bodyBase64?: string;
  headers?: Record<string, string>;
  enqueuedAt: string;
  deliveryAttempt: number;
}

export class CatMQError extends Error {
  status: number;
  code: string;
  title: string;
  detail?: string;
  retryAfter?: number;
}

export class WebhookSignatureError extends Error {}

export class ReceivedMessage {
  readonly queue: string;
  readonly raw: Message & { deliveryAttempt: number; leaseId: string; leaseExpiresAt: string };
  readonly messageId: string;
  readonly serialNumber: string;
  readonly messageType: string | null;
  readonly contentType: string;
  readonly headers: Record<string, string>;
  readonly enqueuedAt: string;
  readonly deliveryAttempt: number;
  readonly leaseId: string;
  leaseExpiresAt: string;
  settled: boolean;
  /** Parsed JSON, a string for other text, or a Uint8Array for binary. */
  readonly body: unknown;
  ack(): Promise<void>;
  nack(): Promise<void>;
  defer(seconds: number): Promise<void>;
  deadLetter(): Promise<void>;
  drop(): Promise<void>;
  notInterested(): Promise<void>;
  extend(seconds: number): Promise<string>;
}

export interface ConsumeOptions {
  max?: number;
  waitSeconds?: number;
  leaseSeconds?: number;
  signal?: AbortSignal;
  untilIdle?: boolean;
  onError?: (error: unknown, message?: ReceivedMessage) => void;
}

export class CatMQ {
  constructor(options: { url: string; apiKey?: string; timeoutMs?: number; fetch?: typeof fetch });

  health(): Promise<{ status: string; version: string }>;

  listQueues(options?: { limit?: number; cursor?: string }): Promise<{ queues: Queue[]; nextCursor: string | null }>;
  iterQueues(options?: { pageSize?: number }): AsyncGenerator<Queue>;
  createQueue(name: string, settings?: QueueSettings): Promise<Queue>;
  getQueue(name: string): Promise<Queue>;
  updateQueue(name: string, changes: QueueSettingsUpdate): Promise<Queue>;
  deleteQueue(name: string): Promise<void>;
  purgeQueue(name: string): Promise<void>;
  redrive(name: string, options?: { max?: number }): Promise<number>;

  publish(queue: string, body: unknown, options?: PublishOptions): Promise<PublishResult>;
  publishBatch(queue: string, messages: OutgoingMessage[]): Promise<PublishResult[]>;

  listMessages(queue: string, options?: { limit?: number; cursor?: string; includeBody?: boolean }): Promise<{ messages: Message[]; nextCursor: string | null }>;
  iterMessages(queue: string, options?: { pageSize?: number; includeBody?: boolean }): AsyncGenerator<Message>;
  getMessage(queue: string, serialNumber: string): Promise<Message>;
  deleteMessage(queue: string, serialNumber: string): Promise<void>;

  receive(queue: string, consumer: string, options?: { max?: number; waitSeconds?: number; leaseSeconds?: number; signal?: AbortSignal }): Promise<ReceivedMessage[]>;
  ack(leaseId: string, disposition?: Disposition | 'defer', options?: { deferSeconds?: number }): Promise<void>;
  ackBatch(acks: Array<string | { leaseId: string; disposition?: Disposition | 'defer'; deferSeconds?: number }>): Promise<Array<{ leaseId: string; status: 'acked' | 'notFound' | 'leaseExpired' }>>;
  extend(leaseId: string, seconds: number): Promise<string>;
  consume(queue: string, consumer: string, handler: (message: ReceivedMessage) => unknown, options?: ConsumeOptions): Promise<number>;

  listConsumers(queue: string): Promise<Consumer[]>;
  deleteConsumer(queue: string, name: string): Promise<void>;

  listWebhooks(queue: string): Promise<Webhook[]>;
  putWebhook(queue: string, name: string, settings: WebhookSettings): Promise<Webhook>;
  getWebhook(queue: string, name: string): Promise<Webhook>;
  deleteWebhook(queue: string, name: string): Promise<void>;
  rotateWebhookSecret(queue: string, name: string): Promise<string>;
}

export function verifyWebhook(
  body: string | Uint8Array | ArrayBuffer,
  signature: string | null | undefined,
  secret: string | string[],
  options?: { toleranceSeconds?: number; now?: number }
): Promise<WebhookDelivery>;

export default CatMQ;
