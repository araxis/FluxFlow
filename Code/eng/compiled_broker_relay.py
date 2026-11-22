"""Protocol-aware loopback relays for compiled broker acceptance tests."""
from collections import Counter
from contextlib import contextmanager
import json
from select import select
from socket import create_connection
from socketserver import BaseRequestHandler, ThreadingTCPServer
from threading import Event, Thread


def read_exact(reader, length):
    data = bytearray()
    while len(data) < length:
        part = reader.read(length - len(data))
        if not part:
            raise EOFError
        data.extend(part)
    return bytes(data)


@contextmanager
def local_relay(upstream, session):
    stopping = Event()

    class Relay(BaseRequestHandler):
        def handle(self):
            with create_connection(upstream, timeout=5) as broker:
                client = self.request
                client.settimeout(5)
                forward = session()
                with client.makefile("rb", buffering=0) as requests, broker.makefile("rb", buffering=0) as replies:
                    try:
                        while not stopping.is_set():
                            for sender in select([client, broker], [], [], .25)[0]:
                                from_client = sender is client
                                frame = forward(requests if from_client else replies, from_client)
                                if frame is None:
                                    return
                                if frame:
                                    (broker if from_client else client).sendall(frame)
                    except (ConnectionError, EOFError):
                        return

    with ThreadingTCPServer(("127.0.0.1", 0), Relay) as server:
        thread = Thread(target=server.serve_forever)
        thread.start()
        try:
            yield server.server_address[1]
        finally:
            stopping.set()
            server.shutdown()
            thread.join()


def drop_publish_confirmation(broker_port, *, subscriber=False, fail_first_reconnect=False):
    reject_connect = Event()
    def session():
        pending = None

        def forward(reader, from_client):
            nonlocal pending
            header = bytearray(read_exact(reader, 1))
            length = 0
            for shift in range(0, 28, 7):
                digit = read_exact(reader, 1)[0]
                header.append(digit)
                length |= (digit & 127) << shift
                if not digit & 128:
                    break
            else:
                raise ValueError("Invalid MQTT packet length")
            body = read_exact(reader, length)
            kind = header[0] >> 4
            if from_client and kind == 1 and reject_connect.is_set():
                reject_connect.clear()
                return None
            if from_client != subscriber and kind == 3 and ((header[0] >> 1) & 3) == 1:
                end = 2 + int.from_bytes(body[:2], "big")
                if body[2:end] == b"trial/uncertain" and not header[0] & 8:
                    pending = body[end:end + 2]
            if from_client == subscriber and kind == 4 and body[:2] == pending:
                if fail_first_reconnect:
                    reject_connect.set()
                return None
            return header + body
        return forward
    return local_relay(("127.0.0.1", broker_port), session)


@contextmanager
def amqp_confirm_relay(port):
    counts = Counter()

    def session():
        awaiting_confirm = False

        def forward(reader, from_client):
            nonlocal awaiting_confirm
            header = read_exact(reader, 7)
            if from_client and header.startswith(b"AMQP"):
                return header + read_exact(reader, 1)
            body = read_exact(reader, int.from_bytes(header[3:7], "big"))
            end = read_exact(reader, 1)
            if from_client and header[0] == 3:
                # The test publishes two distinct message bodies, B then C.
                if body == b"\x00\xff":
                    awaiting_confirm = True
                    counts["B"] += 1
                elif body == b"\x80\x01":
                    counts["C"] += 1
            elif not from_client and header[0] == 1 and body.startswith(b"\x00\x3c\x00\x50"):
                if awaiting_confirm:
                    counts["lost"] += 1
                    return None
                counts["confirmed"] += 1
            return header + body + end
        return forward

    with local_relay(("127.0.0.1", port), session) as local_port:
        yield local_port, counts


@contextmanager
def nats_fault_relay(binding, mode):
    host, port = binding.rsplit(":", 1)
    counts = Counter()
    awaiting_redelivery = False

    def session():
        first_ack_inbox = None
        suppressed = False

        def forward(reader, from_client):
            nonlocal first_ack_inbox, suppressed, awaiting_redelivery
            line = reader.readline(8192)
            if not line:
                return None
            fields = line.split()
            if fields[0] not in (b"PUB", b"HPUB", b"MSG", b"HMSG"):
                return line
            payload = read_exact(reader, int(fields[-1]) + 2)
            if mode in ("publish", "subscribe", "request", "reply"):
                outgoing = mode in ("publish", "reply")
                inbox = mode in ("request", "reply")
                relevant = fields[1].startswith(b"_INBOX.") if inbox else fields[1] in (b"orders.cut", b"orders.fresh")
                if from_client == outgoing and relevant:
                    if payload.endswith(b"\x01\r\n"):
                        counts["dropped"] += 1
                        return None
                    if payload.endswith(b"\x02\r\n"):
                        counts["forwarded"] += 1
            elif mode == "pull":
                if not from_client and fields[0] in (b"MSG", b"HMSG") and any(f.startswith(b"$JS.ACK.") for f in fields):
                    if fields[1] == b"orders.in" and not awaiting_redelivery:
                        awaiting_redelivery = True
                        counts["dropped"] += 1
                        return None
                    if fields[1] == b"orders.end":
                        awaiting_redelivery = False
                    counts["forwarded"] += 1
            elif mode == "settlement":
                if from_client and fields[1].startswith(b"$JS.ACK.") and len(fields) >= 4:
                    counts["requests"] += 1
                    if not suppressed:
                        first_ack_inbox = fields[2]
                if not from_client and fields[1] == first_ack_inbox:
                    suppressed = True
                    first_ack_inbox = None
                    counts["dropped"] += 1
                    return b""
            elif mode == "storage":
                if from_client and fields[1] == b"orders.in":
                    counts["requests"] += 1
                if not from_client and fields[0] == b"MSG" and payload.startswith(b"{"):
                    ack = json.loads(payload)
                    if ack.get("stream") == "UNIQUE" and "seq" in ack:
                        if not ack.get("duplicate"):
                            counts["dropped"] += 1
                            return b""
                        counts["confirmed"] += 1
            return line + payload
        return forward

    with local_relay((host, int(port)), session) as address:
        yield f"nats://127.0.0.1:{address}", counts
