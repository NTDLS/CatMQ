"""A client for the CatMQ HTTP API. It uses only the Python standard library.

    from catmq import CatMQ

    mq = CatMQ("http://localhost:45783", api_key="...")
    mq.create_queue("orders", persistence="persistent", consumption_scheme="firstConsumer")
    mq.publish("orders", {"orderId": 1234}, message_type="orders.created")

    def handle(message):
        print(message.body)

    mq.consume("orders", "order-worker", handle)
"""

from .client import (
    CatMQ,
    CatMQError,
    Page,
    PublishResult,
    ReceivedMessage,
    WebhookSignatureError,
    verify_webhook,
)

__all__ = [
    "CatMQ",
    "CatMQError",
    "Page",
    "PublishResult",
    "ReceivedMessage",
    "WebhookSignatureError",
    "verify_webhook",
]

__version__ = "1.0.0"
