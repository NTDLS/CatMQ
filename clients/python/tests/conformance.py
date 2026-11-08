"""Runs the shared conformance scenarios (clients/conformance/scenarios.json) against a running CatMQ service.

    CATMQ_URL=http://127.0.0.1:45783 CATMQ_API_KEY=... python tests/conformance.py

Prints one JSON line per scenario, and exits with 1 if any failed or is not implemented.
"""

import json
import os
import sys
import threading
import time
import traceback
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

from catmq import CatMQ, CatMQError, WebhookSignatureError, verify_webhook  # noqa: E402

URL = os.environ.get("CATMQ_URL", "http://127.0.0.1:45783")
API_KEY = os.environ.get("CATMQ_API_KEY")
WEBHOOK_HOST = os.environ.get("CATMQ_WEBHOOK_HOST", "127.0.0.1")
SCENARIOS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "conformance", "scenarios.json")

mq = CatMQ(URL, API_KEY)


def new_queue(**settings):
    name = "py" + uuid.uuid4().hex
    mq.create_queue(name, **settings)
    return name


def depth(queue):
    return mq.get_queue(queue)["stats"]["depth"]


def wait_until(condition, seconds=10.0):
    deadline = time.time() + seconds
    while time.time() < deadline:
        if condition():
            return True
        time.sleep(0.05)
    return condition()


def expect_error(status, code, action):
    try:
        action()
    except CatMQError as ex:
        assert ex.status == status, f"Expected status {status}, got {ex.status} ({ex.code})."
        assert ex.code == code, f"Expected code {code}, got {ex.code}."
        assert ex.title, "The error has no title."
        return ex
    raise AssertionError(f"Expected {status} {code}, but nothing was raised.")


def receive_all(queue, consumer, count, **kwargs):
    received = []
    deadline = time.time() + 10
    while len(received) < count and time.time() < deadline:
        received.extend(mq.receive(queue, consumer, max=count - len(received), wait_seconds=2, **kwargs))
    assert len(received) == count, f"Received {len(received)} of {count} messages."
    return received


# ---- Scenarios ----


def health():
    result = CatMQ(URL).health()
    assert result["status"] == "ok"
    assert result["version"]


def unauthorized():
    expect_error(401, "unauthorized", lambda: CatMQ(URL, "not-a-real-key").list_queues())


def queue_lifecycle():
    name = "py" + uuid.uuid4().hex
    created = mq.create_queue(name, consumption_scheme="firstConsumer", max_outstanding_deliveries=3)
    assert created["name"] == name
    assert created["settings"]["consumptionScheme"] == "firstConsumer"
    assert created["settings"]["maxOutstandingDeliveries"] == 3

    again = mq.create_queue(name, consumption_scheme="firstConsumer", max_outstanding_deliveries=4)
    assert again["settings"]["maxOutstandingDeliveries"] == 4

    updated = mq.update_queue(name, max_depth=50)
    assert updated["settings"]["maxDepth"] == 50
    assert updated["settings"]["maxOutstandingDeliveries"] == 4
    assert mq.update_queue(name, max_depth=None)["settings"]["maxDepth"] is None
    assert mq.get_queue(name)["stats"]["depth"] == 0

    first = mq.list_queues(limit=1)
    assert len(first.items) == 1
    assert any(queue["name"] == name for queue in mq.iter_queues(page_size=2))

    mq.delete_queue(name)
    expect_error(404, "queue_not_found", lambda: mq.get_queue(name))


def invalid_request():
    expect_error(400, "invalid_request", lambda: mq.create_queue("py" + uuid.uuid4().hex, persistence="sometimes"))
    expect_error(400, "invalid_request", lambda: mq.receive(new_queue(), "bad name", wait_seconds=0))


def publish_receive_ack():
    queue = new_queue(consumption_scheme="firstConsumer")
    first = mq.publish(queue, {"orderId": 7, "items": ["a", "b"]}, message_type="orders.created", headers={"tenant": "acme"})
    assert first.status == "accepted" and first.message_id
    mq.publish(queue, "hello there", content_type="text/plain")
    mq.publish(queue, bytes([0, 1, 2, 255]))

    messages = sorted(receive_all(queue, "worker", 3), key=lambda m: int(m.serial_number))
    assert messages[0].message_id == first.message_id
    assert messages[0].body == {"orderId": 7, "items": ["a", "b"]}
    assert messages[0].message_type == "orders.created"
    assert messages[0].headers == {"tenant": "acme"}
    assert messages[0].delivery_attempt == 1
    assert messages[0].lease_id
    assert messages[1].body == "hello there"
    assert messages[1].content_type.startswith("text/plain")
    assert messages[2].body == bytes([0, 1, 2, 255])
    assert messages[2].content_type == "application/octet-stream"

    for message in messages:
        message.ack()
    assert wait_until(lambda: depth(queue) == 0)


def batch_publish():
    queue = new_queue()
    results = mq.publish_batch(queue, [
        {"body": {"n": 1}},
        {"body": "", "content_type": "text/plain"},
        {"body": {"n": 3}, "message_type": "third"},
    ])
    assert [result.status for result in results] == ["accepted", "rejected", "accepted"]
    assert results[1].error is not None and results[1].error.code == "invalid_request"
    assert results[0].message_id and results[2].message_id
    assert wait_until(lambda: depth(queue) == 2)


def idempotency():
    queue = new_queue()
    key = uuid.uuid4().hex
    first = mq.publish(queue, {"n": 1}, idempotency_key=key)
    second = mq.publish(queue, {"n": 1}, idempotency_key=key)
    assert first.status == "accepted"
    assert second.status == "duplicate"
    assert first.message_id == second.message_id
    assert depth(queue) == 1


def queue_full():
    queue = new_queue(max_depth=1)
    mq.publish(queue, {"n": 1})
    expect_error(429, "queue_full", lambda: mq.publish(queue, {"n": 2}))


def lease_dispositions():
    queue = new_queue(consumption_scheme="firstConsumer")
    mq.publish(queue, {"n": 1})

    message = receive_all(queue, "worker", 1, lease_seconds=30)[0]
    before = message.lease_expires_at
    expires = message.extend(600)
    assert expires and expires != before

    message.nack()
    again = receive_all(queue, "worker", 1)[0]
    assert again.message_id == message.message_id
    assert again.delivery_attempt == 2

    again.defer(2)
    assert mq.receive(queue, "worker", wait_seconds=1) == []
    later = receive_all(queue, "worker", 1)[0]
    assert later.message_id == message.message_id
    later.ack()

    expect_error(404, "lease_not_found", lambda: mq.ack("no-such-lease"))


def batch_ack():
    queue = new_queue(consumption_scheme="firstConsumer")
    mq.publish_batch(queue, [{"body": {"n": n}} for n in range(3)])
    messages = receive_all(queue, "worker", 3)

    results = mq.ack_batch([messages[0].lease_id, {"lease_id": messages[1].lease_id, "disposition": "drop"},
                            {"lease_id": messages[2].lease_id, "disposition": "consumed"}, "no-such-lease"])
    assert [result["status"] for result in results] == ["acked", "acked", "acked", "notFound"]
    assert wait_until(lambda: depth(queue) == 0)


def consume_loop():
    queue = new_queue(consumption_scheme="firstConsumer", dead_letter={})
    mq.publish_batch(queue, [{"body": {"n": n}} for n in range(5)])

    attempts = {}

    def handler(message):
        n = message.body["n"]
        attempts[n] = attempts.get(n, 0) + 1
        if n == 1 and attempts[n] == 1:
            raise RuntimeError("A failure the loop should recover from.")
        if n == 2:
            return "deadLetter"
        return None

    handled = mq.consume(queue, "loop", handler, max=5, wait_seconds=1, until_idle=True)
    assert handled == 6, f"Handled {handled} times."
    assert attempts[1] == 2
    assert wait_until(lambda: depth(queue) == 0)
    assert wait_until(lambda: depth(queue + ".dlq") == 1)


def dead_letter_redrive():
    queue = new_queue(consumption_scheme="firstConsumer", dead_letter={"persistence": "ephemeral"})
    mq.publish(queue, {"n": 1})
    receive_all(queue, "worker", 1)[0].dead_letter()
    assert wait_until(lambda: depth(queue + ".dlq") == 1 and depth(queue) == 0)
    assert mq.get_queue(queue + ".dlq")["isDeadLetter"] is True

    assert mq.redrive(queue) == 1
    assert wait_until(lambda: depth(queue) == 1 and depth(queue + ".dlq") == 0)


def browse_messages():
    queue = new_queue()
    mq.publish_batch(queue, [{"body": {"n": n}} for n in range(5)])

    first = mq.list_messages(queue, limit=2)
    assert len(first.items) == 2 and first.next_cursor
    assert "body" not in first.items[0]
    all_messages = list(mq.iter_messages(queue, page_size=2, include_body=True))
    assert [message["body"]["n"] for message in all_messages] == [0, 1, 2, 3, 4]

    serial = all_messages[2]["serialNumber"]
    assert mq.get_message(queue, serial)["body"] == {"n": 2}
    mq.delete_message(queue, serial)
    expect_error(404, "message_not_found", lambda: mq.get_message(queue, serial))
    assert depth(queue) == 4

    mq.purge_queue(queue)
    assert depth(queue) == 0


def consumers():
    queue = new_queue()
    assert mq.receive(queue, "reporting", wait_seconds=0) == []
    listed = mq.list_consumers(queue)
    assert [(c["name"], c["kind"]) for c in listed] == [("reporting", "lease")]
    mq.delete_consumer(queue, "reporting")
    assert mq.list_consumers(queue) == []


def webhooks():
    deliveries = []

    class Receiver(BaseHTTPRequestHandler):
        def do_POST(self):
            body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
            deliveries.append((body, self.headers.get("X-CatMQ-Signature")))
            self.send_response(200)
            self.send_header("Content-Length", "0")
            self.end_headers()

        def log_message(self, *args):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Receiver)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        queue = new_queue()
        url = f"http://{WEBHOOK_HOST}:{server.server_address[1]}/hook"

        created = mq.put_webhook(queue, "orders", url, timeout_seconds=10, headers={"Authorization": "Bearer token"})
        secret = created["secret"]
        assert secret.startswith("whsec_")
        assert created["headers"] == {"Authorization": "********"}
        assert "secret" not in mq.get_webhook(queue, "orders")
        assert [w["name"] for w in mq.list_webhooks(queue)] == ["orders"]

        mq.publish(queue, {"orderId": 9}, message_type="orders.created")
        assert wait_until(lambda: len(deliveries) == 1)
        body, signature = deliveries[0]

        delivery = verify_webhook(body, signature, secret)
        assert delivery["queue"] == queue
        assert delivery["body"] == {"orderId": 9}
        assert delivery["messageType"] == "orders.created"
        assert delivery["deliveryAttempt"] == 1

        for bad in (lambda: verify_webhook(body, signature, "whsec_wrong"),
                    lambda: verify_webhook(body, signature, secret, now=time.time() + 3600),
                    lambda: verify_webhook(body + b" ", signature, secret),
                    lambda: verify_webhook(body, None, secret)):
            try:
                bad()
            except WebhookSignatureError:
                continue
            raise AssertionError("A bad delivery was accepted.")

        new_secret = mq.rotate_webhook_secret(queue, "orders")
        assert new_secret.startswith("whsec_") and new_secret != secret
        mq.publish(queue, {"orderId": 10})
        assert wait_until(lambda: len(deliveries) == 2)
        verify_webhook(deliveries[1][0], deliveries[1][1], new_secret)
        verify_webhook(deliveries[1][0], deliveries[1][1], [secret])

        mq.delete_webhook(queue, "orders")
        expect_error(404, "webhook_not_found", lambda: mq.get_webhook(queue, "orders"))
    finally:
        server.shutdown()


def main():
    with open(SCENARIOS, encoding="utf-8") as file:
        ids = [scenario["id"] for scenario in json.load(file)["scenarios"]]

    failed = False
    for scenario_id in ids:
        scenario = globals().get(scenario_id)
        result = {"scenario": scenario_id, "passed": False}
        if scenario is None:
            result["error"] = "Not implemented."
        else:
            try:
                scenario()
                result["passed"] = True
            except Exception as ex:
                result["error"] = f"{type(ex).__name__}: {ex}\n{traceback.format_exc()}"
        failed |= not result["passed"]
        print(json.dumps(result), flush=True)

    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
