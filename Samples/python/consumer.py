"""Consumes the sample messages with the Python client.

    python consumer.py [--until-idle] [--manual]

By default it uses the client's consume loop. With --manual it receives and acknowledges each message itself.
"""

import os
import sys

try:
    from catmq import CatMQ
except ImportError:  # Not installed: use the client in this repository.
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "clients", "python"))
    from catmq import CatMQ

QUEUE = "samples.orders"
CONSUMER = "python-worker"  # Consumers that share a name compete for messages, like several workers of one service.

mq = CatMQ(os.environ.get("CATMQ_URL", "http://127.0.0.1:45783"), api_key=os.environ.get("CATMQ_API_KEY"))
mq.create_queue(QUEUE, persistence="persistent", consumption_scheme="firstConsumer",
                max_outstanding_deliveries=10, dead_letter={"persistence": "persistent"})

until_idle = "--until-idle" in sys.argv
wait_seconds = 7 if until_idle else 20


def handle(message):
    """Returns a disposition. Returning None acknowledges the message; raising has it delivered again."""
    source = message.headers.get("source", "unknown")

    if message.message_type == "orders.created":
        order = message.body  # JSON arrives parsed.
        if not order.get("customer"):
            print(f"Order {order['orderId']} from {source} has no customer: dead-lettering it.")
            return "deadLetter"
        print(f"Order {order['orderId']} for {order['customer']}, {order['total']:.2f}, from {source} (attempt {message.delivery_attempt}).")
        return None

    if message.message_type == "orders.note":
        print(f"Note from {source}: {message.body}")  # Text arrives as a str.
        return None

    if message.message_type == "orders.receipt":
        data = message.body  # Binary arrives as bytes.
        print(f"Receipt from {source}: {len(data)} bytes of {message.content_type}, starting \"{data[:8].decode('ascii', 'replace')}\".")
        return None

    return "notInterested"  # Not for this consumer. Another one may want it.


print(f"Consuming {QUEUE} until it is idle..." if until_idle else f"Consuming {QUEUE}. Press Ctrl+C to stop.")

try:
    if "--manual" in sys.argv:
        # Receive leases up to max messages for lease_seconds. A message that is not settled before its lease
        # ends is delivered again; message.extend(seconds) keeps it longer.
        while True:
            messages = mq.receive(QUEUE, CONSUMER, max=10, wait_seconds=wait_seconds, lease_seconds=60)
            if not messages and until_idle:
                break
            for message in messages:
                try:
                    disposition = handle(message)
                except Exception:
                    message.defer(30)  # Try again in 30 seconds, without counting a failed attempt.
                    continue
                if disposition is None:
                    message.ack()
                else:
                    mq.ack(message.lease_id, disposition)
    else:
        mq.consume(QUEUE, CONSUMER, handle, max=10, wait_seconds=wait_seconds, until_idle=until_idle)
except KeyboardInterrupt:
    pass
