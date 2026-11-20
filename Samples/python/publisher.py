"""Publishes the sample messages with the Python client.

    python publisher.py [count]
"""

import os
import sys

try:
    from catmq import CatMQ
except ImportError:  # Not installed: use the client in this repository.
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "clients", "python"))
    from catmq import CatMQ

QUEUE = "samples.orders"

mq = CatMQ(os.environ.get("CATMQ_URL", "http://127.0.0.1:45783"), api_key=os.environ.get("CATMQ_API_KEY"))

# Creates the queue, or applies these settings to it. Every sample uses the same ones.
mq.create_queue(QUEUE, persistence="persistent", consumption_scheme="firstConsumer",
                max_outstanding_deliveries=10, dead_letter={"persistence": "persistent"})

count = int(sys.argv[1]) if len(sys.argv) > 1 else 5
headers = {"source": "python"}

# One message per request. A dict is sent as JSON.
mq.publish(QUEUE, {"orderId": 1, "customer": "Ada", "total": 10.5}, message_type="orders.created", headers=headers)

# Up to 1,000 per request. The idempotency keys make the request safe to retry: a key seen in the last
# 24 hours is reported as a duplicate, not stored again.
results = mq.publish_batch(QUEUE, [
    *({"body": {"orderId": i, "customer": "Ada", "total": 10.5 * i}, "message_type": "orders.created",
       "headers": headers, "idempotency_key": f"python-order-{i}"} for i in range(2, count + 1)),
    {"body": {"orderId": 999, "total": 1}, "message_type": "orders.created", "headers": headers},  # No customer: dead-lettered.
    {"body": "Order 1 is a gift; please wrap it.", "content_type": "text/plain", "message_type": "orders.note", "headers": headers},
    {"body": b"%PDF-1.7 receipt", "message_type": "orders.receipt", "headers": headers},  # bytes are sent as binary.
    {"body": "Sent with a 5 second delay.", "content_type": "text/plain", "message_type": "orders.note",
     "headers": headers, "defer_seconds": 5},
])

accepted = 1 + sum(result.status == "accepted" for result in results)
duplicates = sum(result.status == "duplicate" for result in results)
print(f"Published {accepted} messages to {QUEUE}" + (f" ({duplicates} were duplicates of earlier runs)." if duplicates else "."))
