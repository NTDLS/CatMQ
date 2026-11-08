import base64
import hashlib
import hmac
import http.client
import json
import logging
import threading
import time
from dataclasses import dataclass, field
from typing import Any, Callable, Dict, Iterable, Iterator, List, Optional, Sequence, Union
from urllib.parse import quote, urlencode, urlsplit

_log = logging.getLogger("catmq")

_DISPOSITIONS = ("consumed", "notConsumed", "notInterested", "deadLetter", "drop", "defer")


class CatMQError(Exception):
    """An error response from CatMQ (a problem details document), or a failure to reach it.

    status is the HTTP status (0 when CatMQ could not be reached), code is CatMQ's stable error code, such as
    queue_not_found or queue_full, and retry_after is the seconds to wait before retrying, when CatMQ gave one.
    """

    def __init__(self, status: int, code: str, title: str, detail: Optional[str] = None, retry_after: Optional[float] = None):
        super().__init__(f"{title} ({status} {code})" if status else title)
        self.status = status
        self.code = code
        self.title = title
        self.detail = detail
        self.retry_after = retry_after


class WebhookSignatureError(Exception):
    """A webhook delivery whose signature is missing, wrong, or too old."""


@dataclass
class Page:
    """One page of a list. Pass next_cursor back to get the next page; it is None on the last one."""

    items: List[Dict[str, Any]]
    next_cursor: Optional[str]


@dataclass
class PublishResult:
    """What happened to one published message: status is accepted, duplicate or rejected."""

    status: str
    message_id: Optional[str] = None
    error: Optional[CatMQError] = None


def _camel(name: str) -> str:
    head, *rest = name.split("_")
    return head + "".join(part[:1].upper() + part[1:] for part in rest)


def _camel_keys(values: Dict[str, Any]) -> Dict[str, Any]:
    """Settings may be given in snake_case (max_depth) or as the API names them (maxDepth)."""
    return {_camel(key): (_camel_keys(value) if isinstance(value, dict) else value) for key, value in values.items()}


def _segment(value: str) -> str:
    return quote(str(value), safe="")


def _message(body: Any = None, *, content_type: Optional[str] = None, message_type: Optional[str] = None,
             headers: Optional[Dict[str, str]] = None, defer_seconds: Optional[int] = None,
             idempotency_key: Optional[str] = None) -> Dict[str, Any]:
    """A message in the API's form. bytes are sent as bodyBase64; anything else is sent as body."""
    message: Dict[str, Any] = {}
    if isinstance(body, (bytes, bytearray, memoryview)):
        message["bodyBase64"] = base64.b64encode(bytes(body)).decode("ascii")
        message["contentType"] = content_type or "application/octet-stream"
    else:
        message["body"] = body
        if content_type is not None:
            message["contentType"] = content_type
    if message_type is not None:
        message["messageType"] = message_type
    if headers is not None:
        message["headers"] = headers
    if defer_seconds is not None:
        message["deferSeconds"] = defer_seconds
    if idempotency_key is not None:
        message["idempotencyKey"] = idempotency_key
    return message


class ReceivedMessage:
    """A message received under a lease. Settle it with one of ack, nack, defer, dead_letter, drop or not_interested
    before the lease expires, or extend the lease. Otherwise it is delivered again."""

    def __init__(self, client: "CatMQ", queue: str, data: Dict[str, Any]):
        self._client = client
        self.queue = queue
        self.raw = data
        self.message_id: str = data["messageId"]
        self.serial_number: str = data["serialNumber"]
        self.message_type: Optional[str] = data.get("messageType")
        self.content_type: str = data.get("contentType", "application/json")
        self.headers: Dict[str, str] = data.get("headers") or {}
        self.enqueued_at: str = data.get("enqueuedAt", "")
        self.delivery_attempt: int = data.get("deliveryAttempt", 1)
        self.lease_id: str = data.get("leaseId", "")
        self.lease_expires_at: Optional[str] = data.get("leaseExpiresAt")
        self.settled = False

        # JSON bodies arrive parsed, other text as a string, and binary bodies as bytes.
        if "bodyBase64" in data:
            self.body: Any = base64.b64decode(data["bodyBase64"])
        else:
            self.body = data.get("body")

    def __repr__(self) -> str:
        return f"ReceivedMessage(queue={self.queue!r}, message_id={self.message_id!r}, delivery_attempt={self.delivery_attempt})"

    def _settle(self, disposition: str, defer_seconds: Optional[int] = None) -> None:
        self._client.ack(self.lease_id, disposition, defer_seconds=defer_seconds)
        self.settled = True

    def ack(self) -> None:
        """The message was processed, so it is removed."""
        self._settle("consumed")

    def nack(self) -> None:
        """The message was not processed. It is delivered again, which counts as a failed attempt."""
        self._settle("notConsumed")

    def defer(self, seconds: int) -> None:
        """Deliver the message again after the given number of seconds, without counting a failed attempt."""
        self._settle("defer", defer_seconds=seconds)

    def dead_letter(self) -> None:
        """Move the message to the queue's dead-letter queue."""
        self._settle("deadLetter")

    def drop(self) -> None:
        """Remove the message without processing it."""
        self._settle("drop")

    def not_interested(self) -> None:
        """This consumer does not want the message; it goes to another consumer."""
        self._settle("notInterested")

    def extend(self, seconds: int) -> str:
        """Extend the lease to the given number of seconds from now. Returns the new expiry."""
        self.lease_expires_at = self._client.extend(self.lease_id, seconds)
        return self.lease_expires_at


class CatMQ:
    """A client for the CatMQ HTTP API. One instance can be shared by threads; each thread keeps its own connection.

    url is the service's base URL, such as http://localhost:45783. timeout is in seconds; a receive that waits for
    messages allows for its wait on top of it.
    """

    def __init__(self, url: str, api_key: Optional[str] = None, *, timeout: float = 30.0):
        parts = urlsplit(url.rstrip("/"))
        if parts.scheme not in ("http", "https") or not parts.hostname:
            raise ValueError(f"Not an http or https URL: {url}")
        self._scheme = parts.scheme
        self._host = parts.hostname
        self._port = parts.port
        self._base_path = (parts.path or "") + "/api/v1"
        self._api_key = api_key
        self._timeout = timeout
        self._local = threading.local()

    # ---- HTTP ----

    def _connection(self, timeout: float) -> http.client.HTTPConnection:
        connection = getattr(self._local, "connection", None)
        if connection is None:
            factory = http.client.HTTPSConnection if self._scheme == "https" else http.client.HTTPConnection
            connection = factory(self._host, self._port, timeout=timeout)
            self._local.connection = connection
            self._local.used = False
        connection.timeout = timeout
        if connection.sock is not None:
            connection.sock.settimeout(timeout)
        return connection

    def _drop_connection(self) -> None:
        connection = getattr(self._local, "connection", None)
        if connection is not None:
            connection.close()
        self._local.connection = None

    def close(self) -> None:
        """Closes this thread's connection. The client still works afterwards; it connects again when needed."""
        self._drop_connection()

    def __enter__(self) -> "CatMQ":
        return self

    def __exit__(self, *_: Any) -> None:
        self.close()

    def _request(self, method: str, path: str, body: Any = None, *, query: Optional[Dict[str, Any]] = None,
                 extra_timeout: float = 0, authenticate: bool = True) -> Any:
        target = self._base_path + path
        if query:
            target += "?" + urlencode({key: value for key, value in query.items() if value is not None})

        headers = {"Accept": "application/json"}
        payload = None
        if body is not None:
            payload = json.dumps(body, separators=(",", ":")).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if authenticate and self._api_key:
            headers["Authorization"] = f"Bearer {self._api_key}"

        for attempt in (1, 2):
            connection = self._connection(self._timeout + extra_timeout)
            reused = getattr(self._local, "used", False)
            try:
                connection.request(method, target, body=payload, headers=headers)
                response = connection.getresponse()
                data = response.read()
                self._local.used = True
            except (http.client.RemoteDisconnected, ConnectionResetError, BrokenPipeError) as ex:
                # A kept-alive connection the server had already closed. Only then is it safe to send again,
                # because the request never reached the server.
                self._drop_connection()
                if reused and attempt == 1:
                    continue
                raise CatMQError(0, "connection_failed", f"Could not reach CatMQ: {ex}") from ex
            except OSError as ex:
                self._drop_connection()
                raise CatMQError(0, "connection_failed", f"Could not reach CatMQ: {ex}") from ex

            if response.getheader("Connection", "").lower() == "close":
                self._drop_connection()

            if response.status >= 400:
                raise self._error(response, data)
            if not data:
                return None
            return json.loads(data)

        raise AssertionError("unreachable")

    @staticmethod
    def _error(response: http.client.HTTPResponse, data: bytes) -> CatMQError:
        retry_after = None
        header = response.getheader("Retry-After")
        if header and header.strip().isdigit():
            retry_after = float(header.strip())
        try:
            problem = json.loads(data) if data else {}
        except ValueError:
            problem = {}
        if not isinstance(problem, dict):
            problem = {}
        return CatMQError(
            response.status,
            problem.get("code") or f"http_{response.status}",
            problem.get("title") or response.reason or "Request failed.",
            problem.get("detail"),
            retry_after,
        )

    # ---- System ----

    def health(self) -> Dict[str, Any]:
        """The service's status and version. This needs no API key."""
        return self._request("GET", "/health", authenticate=False)

    # ---- Queues ----

    def list_queues(self, *, limit: Optional[int] = None, cursor: Optional[str] = None) -> Page:
        result = self._request("GET", "/queues", query={"limit": limit, "cursor": cursor})
        return Page(result["queues"], result.get("nextCursor"))

    def iter_queues(self, *, page_size: int = 100) -> Iterator[Dict[str, Any]]:
        """Every queue, fetching pages as needed."""
        cursor = None
        while True:
            page = self.list_queues(limit=page_size, cursor=cursor)
            yield from page.items
            if page.next_cursor is None:
                return
            cursor = page.next_cursor

    def create_queue(self, name: str, **settings: Any) -> Dict[str, Any]:
        """Creates a queue, or replaces the settings of an existing one; settings left out return to their defaults.
        Settings may be snake_case: persistence, consumption_scheme, delivery_scheme, max_outstanding_deliveries,
        max_delivery_attempts, max_message_age_seconds, delivery_throttle_milliseconds, max_depth and dead_letter."""
        return self._request("PUT", f"/queues/{_segment(name)}", _camel_keys(settings))

    def get_queue(self, name: str) -> Dict[str, Any]:
        """A queue's settings and stats."""
        return self._request("GET", f"/queues/{_segment(name)}")

    def update_queue(self, name: str, **changes: Any) -> Dict[str, Any]:
        """Changes only the settings given. None removes a limit (max_message_age_seconds, max_depth)."""
        return self._request("PATCH", f"/queues/{_segment(name)}", _camel_keys(changes))

    def delete_queue(self, name: str) -> None:
        self._request("DELETE", f"/queues/{_segment(name)}")

    def purge_queue(self, name: str) -> None:
        """Removes every message from a queue."""
        self._request("DELETE", f"/queues/{_segment(name)}/messages")

    def redrive(self, name: str, max: Optional[int] = None) -> int:
        """Moves messages from a queue's dead-letter queue back to it. Returns how many were moved."""
        return self._request("POST", f"/queues/{_segment(name)}/redrive", {"max": max} if max is not None else {})["moved"]

    # ---- Publishing ----

    def publish(self, queue: str, body: Any, *, content_type: Optional[str] = None, message_type: Optional[str] = None,
                headers: Optional[Dict[str, str]] = None, defer_seconds: Optional[int] = None,
                idempotency_key: Optional[str] = None) -> PublishResult:
        """Publishes one message. A dict, list, number or other JSON value is sent as JSON; a string with a text
        content_type is sent as text; bytes are sent as binary. Raises CatMQError with code queue_full (429) when
        the queue is at its maximum depth."""
        message = _message(body, content_type=content_type, message_type=message_type, headers=headers,
                           defer_seconds=defer_seconds, idempotency_key=idempotency_key)
        return self._publish_result(self._request("POST", f"/queues/{_segment(queue)}/messages", message)["results"][0])

    def publish_batch(self, queue: str, messages: Iterable[Dict[str, Any]]) -> List[PublishResult]:
        """Publishes up to 1000 messages in one request. Each is a dict with body and, optionally, content_type,
        message_type, headers, defer_seconds and idempotency_key. Returns a result per message, in order; an invalid
        message is rejected (with its error) without failing the others."""
        items = [_message(**message) for message in messages]
        result = self._request("POST", f"/queues/{_segment(queue)}/messages", {"messages": items})
        return [self._publish_result(item) for item in result["results"]]

    @staticmethod
    def _publish_result(item: Dict[str, Any]) -> PublishResult:
        error = item.get("error")
        return PublishResult(
            item["status"],
            item.get("messageId"),
            CatMQError(error.get("status", 0), error.get("code", ""), error.get("title", "")) if error else None,
        )

    # ---- Browsing ----

    def list_messages(self, queue: str, *, limit: Optional[int] = None, cursor: Optional[str] = None,
                      include_body: bool = False) -> Page:
        """The messages waiting in a queue, oldest first, without leasing them."""
        result = self._request("GET", f"/queues/{_segment(queue)}/messages",
                               query={"limit": limit, "cursor": cursor, "includeBody": "true" if include_body else None})
        return Page(result["messages"], result.get("nextCursor"))

    def iter_messages(self, queue: str, *, page_size: int = 100, include_body: bool = False) -> Iterator[Dict[str, Any]]:
        cursor = None
        while True:
            page = self.list_messages(queue, limit=page_size, cursor=cursor, include_body=include_body)
            yield from page.items
            if page.next_cursor is None:
                return
            cursor = page.next_cursor

    def get_message(self, queue: str, serial_number: str) -> Dict[str, Any]:
        return self._request("GET", f"/queues/{_segment(queue)}/messages/{_segment(serial_number)}")

    def delete_message(self, queue: str, serial_number: str) -> None:
        self._request("DELETE", f"/queues/{_segment(queue)}/messages/{_segment(serial_number)}")

    # ---- Receiving ----

    def receive(self, queue: str, consumer: str, *, max: int = 1, wait_seconds: int = 20,
                lease_seconds: int = 60) -> List[ReceivedMessage]:
        """Leases up to max messages for the named consumer, waiting up to wait_seconds (at most 60) for one to
        arrive. Returns an empty list if none did. Consumers with the same name share the messages."""
        result = self._request("POST", f"/queues/{_segment(queue)}/messages/receive",
                               {"consumer": consumer, "max": max, "waitSeconds": wait_seconds, "leaseSeconds": lease_seconds},
                               extra_timeout=wait_seconds)
        return [ReceivedMessage(self, queue, item) for item in result["messages"]]

    def ack(self, lease_id: str, disposition: str = "consumed", *, defer_seconds: Optional[int] = None) -> None:
        """Settles a lease. disposition is consumed, notConsumed, notInterested, deadLetter, drop or defer
        (with defer_seconds). Raises lease_not_found (404) or lease_expired (409)."""
        body: Dict[str, Any] = {"disposition": disposition}
        if defer_seconds is not None:
            body["deferSeconds"] = defer_seconds
        self._request("POST", f"/leases/{_segment(lease_id)}/ack", body)

    def ack_batch(self, acks: Sequence[Union[str, Dict[str, Any]]]) -> List[Dict[str, str]]:
        """Settles up to 1000 leases at once. Each is a lease ID (consumed) or a dict with lease_id, disposition and
        defer_seconds. Returns {leaseId, status} for each, where status is acked, notFound or leaseExpired."""
        items = []
        for ack in acks:
            if isinstance(ack, str):
                items.append({"leaseId": ack, "disposition": "consumed"})
            else:
                item = _camel_keys(ack)
                item.setdefault("disposition", "consumed")
                items.append(item)
        return self._request("POST", "/leases/ack", {"acks": items})["results"]

    def extend(self, lease_id: str, seconds: int) -> str:
        """Extends a lease to the given number of seconds from now. Returns the new expiry."""
        return self._request("POST", f"/leases/{_segment(lease_id)}/extend", {"leaseSeconds": seconds})["leaseExpiresAt"]

    def consume(self, queue: str, consumer: str, handler: Callable[[ReceivedMessage], Optional[str]], *,
                max: int = 10, wait_seconds: int = 20, lease_seconds: int = 60,
                stop: Optional[threading.Event] = None, until_idle: bool = False) -> int:
        """Receives and handles messages until stop is set, or, with until_idle, until a receive finds none.

        The handler is called with each message. When it returns, the message is acknowledged as consumed, or with
        the disposition it returns (such as "drop" or "deadLetter"). If it raises, the message is delivered again
        (notConsumed). A handler can also settle the message itself, such as with message.defer(30). Returns how many
        messages were handled. Connection failures and server errors are retried with a growing delay.
        """
        handled = 0
        backoff = 1.0
        while stop is None or not stop.is_set():
            try:
                messages = self.receive(queue, consumer, max=max, wait_seconds=wait_seconds, lease_seconds=lease_seconds)
            except CatMQError as ex:
                if ex.status != 0 and ex.status < 500 and ex.status != 429:
                    raise
                delay = ex.retry_after or backoff
                _log.warning("CatMQ receive failed, retrying in %.0fs: %s", delay, ex)
                if stop is not None:
                    stop.wait(delay)
                else:
                    time.sleep(delay)
                backoff = min(backoff * 2, 30.0)
                continue
            backoff = 1.0

            if not messages:
                if until_idle:
                    return handled
                continue

            acks = []
            for message in messages:
                try:
                    disposition = handler(message)
                except Exception:
                    _log.exception("The handler failed for message %s; it will be delivered again.", message.message_id)
                    disposition = "notConsumed"
                handled += 1
                if message.settled:
                    continue
                if disposition is None or disposition is True:
                    disposition = "consumed"
                if disposition not in _DISPOSITIONS or disposition == "defer":
                    raise ValueError(f"The handler returned {disposition!r}; return a disposition, or call message.defer(seconds).")
                acks.append({"leaseId": message.lease_id, "disposition": disposition})

            if acks:
                for result in self.ack_batch(acks):
                    if result["status"] != "acked":
                        _log.warning("Lease %s was %s when acknowledged; the message may be delivered again.", result["leaseId"], result["status"])
        return handled

    # ---- Consumers ----

    def list_consumers(self, queue: str) -> List[Dict[str, Any]]:
        """The queue's consumers: {name, kind, connections}, where kind is tcp, lease or webhook."""
        return self._request("GET", f"/queues/{_segment(queue)}/consumers")["consumers"]

    def delete_consumer(self, queue: str, name: str) -> None:
        self._request("DELETE", f"/queues/{_segment(queue)}/consumers/{_segment(name)}")

    # ---- Webhooks ----

    def list_webhooks(self, queue: str) -> List[Dict[str, Any]]:
        return self._request("GET", f"/queues/{_segment(queue)}/webhooks")["webhooks"]

    def put_webhook(self, queue: str, name: str, url: str, *, timeout_seconds: Optional[int] = None,
                    max_concurrency: Optional[int] = None, headers: Optional[Dict[str, str]] = None,
                    enabled: Optional[bool] = None) -> Dict[str, Any]:
        """Creates a webhook, or replaces its settings. When it is created, the result includes its signing secret,
        which is never returned again; keep it to verify deliveries with verify_webhook."""
        settings = {"url": url, "timeoutSeconds": timeout_seconds, "maxConcurrency": max_concurrency,
                    "headers": headers, "enabled": enabled}
        return self._request("PUT", f"/queues/{_segment(queue)}/webhooks/{_segment(name)}",
                             {key: value for key, value in settings.items() if value is not None})

    def get_webhook(self, queue: str, name: str) -> Dict[str, Any]:
        return self._request("GET", f"/queues/{_segment(queue)}/webhooks/{_segment(name)}")

    def delete_webhook(self, queue: str, name: str) -> None:
        self._request("DELETE", f"/queues/{_segment(queue)}/webhooks/{_segment(name)}")

    def rotate_webhook_secret(self, queue: str, name: str) -> str:
        """Replaces the signing secret. For the next 24 hours deliveries are signed with both, so receivers can switch."""
        return self._request("POST", f"/queues/{_segment(queue)}/webhooks/{_segment(name)}/rotate-secret")["secret"]


def verify_webhook(body: Union[bytes, str], signature: Optional[str], secret: Union[str, Sequence[str]], *,
                   tolerance_seconds: int = 300, now: Optional[float] = None) -> Dict[str, Any]:
    """Checks a webhook delivery's X-CatMQ-Signature header against the raw request body, and returns the delivery.

    Pass the body exactly as it was received, before any parsing. secret may be a list, to accept either secret while
    moving to a new one. Deliveries signed more than tolerance_seconds ago are rejected, so that a captured delivery
    cannot be replayed later. Raises WebhookSignatureError if the delivery is not genuine.
    """
    raw = body.encode("utf-8") if isinstance(body, str) else bytes(body)
    if not signature:
        raise WebhookSignatureError("The delivery has no X-CatMQ-Signature header.")

    timestamp = None
    signatures = []
    for part in signature.split(","):
        key, _, value = part.strip().partition("=")
        if key == "t":
            timestamp = value
        elif key == "v1":
            signatures.append(value)

    if timestamp is None or not timestamp.isdigit() or not signatures:
        raise WebhookSignatureError("The X-CatMQ-Signature header is not valid.")

    current = time.time() if now is None else now
    if abs(current - int(timestamp)) > tolerance_seconds:
        raise WebhookSignatureError("The delivery's timestamp is outside the tolerance.")

    secrets = [secret] if isinstance(secret, str) else list(secret)
    signed = timestamp.encode("ascii") + b"." + raw
    for candidate in secrets:
        expected = hmac.new(candidate.encode("utf-8"), signed, hashlib.sha256).hexdigest()
        if any(hmac.compare_digest(expected, given) for given in signatures):
            return json.loads(raw)

    raise WebhookSignatureError("The delivery's signature does not match the secret.")
