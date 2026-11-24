import argparse, base64, json, os, re, secrets, shutil, signal, socket, sqlite3, subprocess, threading, time, uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.parse import quote

QUEUE = "compiled.order.trial"


def until(check, label, seconds=50):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        answer = check()
        if answer:
            return answer
        time.sleep(0.15)
    raise RuntimeError("Timed out waiting for " + label)


def trial(root, major, control_stop=False):
    exe = root / f"net{major}" / "App.exe"
    evidence = root / (socket.gethostname() + f"-net{major}-" + uuid.uuid4().hex[:8])
    evidence.mkdir()
    assert exe.is_file()
    print("Evidence:", evidence, flush=True)
    env = os.environ.copy()
    runtime = env.get(f"TRIAL_DOTNET{major}")
    if runtime:
        env.update(DOTNET_ROOT=runtime, DOTNET_ROOT_X64=runtime)
    env.update(
        DOTNET_ROLL_FORWARD="LatestPatch",
        DOTNET_MULTILEVEL_LOOKUP="0",
        TRIAL_AMQP_USER="trial",
        TRIAL_AMQP_PASS=secrets.token_urlsafe(24),
    )
    denv = dict(
        os.environ,
        RABBITMQ_DEFAULT_USER="trial",
        RABBITMQ_DEFAULT_PASS=env["TRIAL_AMQP_PASS"],
    )
    docker = shutil.which("docker") or str(
        Path(os.environ["ProgramFiles"]) / "Docker/Docker/resources/bin/docker.exe"
    )
    denv["PATH"] = str(Path(docker).parent) + os.pathsep + denv["PATH"]
    name = "order-restart-" + uuid.uuid4().hex[:12]

    def d(*args, timeout=60):
        return subprocess.check_output(
            [docker, *args],
            text=True,
            env=denv,
            timeout=timeout,
            stderr=subprocess.STDOUT,
        ).strip()

    db = evidence / "effects.sqlite"

    def rows(sql):
        with sqlite3.connect(db) as con:
            return con.execute(sql).fetchall()

    with sqlite3.connect(db) as con:
        con.executescript(
            "CREATE TABLE effects(receipt INTEGER PRIMARY KEY,item TEXT,units INTEGER);CREATE TABLE processed(key TEXT PRIMARY KEY,body TEXT,receipt INTEGER);CREATE TABLE requests(seq INTEGER PRIMARY KEY,key TEXT,receipt INTEGER,replay INTEGER,body TEXT);"
        )

    class Endpoint(BaseHTTPRequestHandler):
        def log_message(self, *a):
            pass

        def do_POST(self):
            assert self.path == "/apply"
            key = self.headers["Idempotency-Key"]
            payload = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            assert payload["item"] == key and key in ("B", "C")
            body = json.dumps(payload, sort_keys=True)
            with sqlite3.connect(db) as con:
                con.execute("BEGIN IMMEDIATE")
                saved = con.execute(
                    "SELECT body,receipt FROM processed WHERE key=?", (key,)
                ).fetchone()
                if saved:
                    assert saved[0] == body
                    receipt = saved[1]
                else:
                    receipt = con.execute(
                        "INSERT INTO effects(item,units) VALUES(?,?)",
                        (key, payload["units"]),
                    ).lastrowid
                    con.execute(
                        "INSERT INTO processed VALUES(?,?,?)", (key, body, receipt)
                    )
                con.execute(
                    "INSERT INTO requests(key,receipt,replay,body) VALUES(?,?,?,?)",
                    (key, receipt, int(bool(saved)), body),
                )
            data = json.dumps(
                dict(item=key, receipt=receipt), separators=(",", ":")
            ).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

    http = ThreadingHTTPServer(("127.0.0.1", 48232), Endpoint)
    threading.Thread(target=http.serve_forever, daemon=True).start()
    container = False
    proc = None
    log = None
    path = None
    summary = {"host": socket.gethostname(), "target": major, "broker": [], "exits": []}

    def start(attempt):
        nonlocal proc, log, path
        path = evidence / f"run-{attempt}.log"
        log = path.open("wb")
        proc = subprocess.Popen(
            [exe],
            cwd=exe.parent,
            stdin=subprocess.DEVNULL if control_stop else subprocess.PIPE,
            creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if control_stop else 0,
            stdout=log,
            stderr=subprocess.STDOUT,
            env=env,
        )

        def listener():
            if proc.poll() is not None:
                raise RuntimeError("EXE exited during start; see " + str(path))
            return re.search(
                r"Now listening on: (http://127\.0\.0\.1:\d+)",
                path.read_text(encoding="utf-8", errors="replace"),
            )

        return until(listener, "listener").group(1)

    def read():
        return path.read_text(encoding="utf-8", errors="replace")

    def stop():
        nonlocal proc, log
        if control_stop:
            proc.send_signal(signal.CTRL_BREAK_EVENT)
            proc.wait(timeout=45)
        else:
            proc.communicate(b"\n", timeout=40)
        code = proc.returncode
        summary["exits"].append(code)
        log.close()
        proc = log = None
        assert code == 0, ("Stop failed", code, path)

    def deliveries(s):
        return re.findall(
            r"Trial\.Delivery\[0\] DELIVERY (\w+) ([a-fA-F0-9]+) (first|again)", s
        )

    def acks(s):
        return re.findall(r"Trial\.Delivery\[0\] ACK ([a-fA-F0-9]+)", s)

    def replies(s):
        return [
            json.loads(b)
            for b in re.findall(r"Trial\.Delivery\[0\] HTTP 200 (\{[^\r\n]+\})", s)
        ]

    try:
        d(
            "run",
            "--rm",
            "-d",
            "--name",
            name,
            "-p",
            "127.0.0.1:48231:5672",
            "-p",
            "127.0.0.1::15672",
            "-e",
            "RABBITMQ_DEFAULT_USER",
            "-e",
            "RABBITMQ_DEFAULT_PASS",
            "rabbitmq:4.1.0-management",
            timeout=180,
        )
        container = True
        port = d("port", name, "15672/tcp").split(":")[-1]
        api = "http://127.0.0.1:" + port + "/api"
        authorization = (
            "Basic "
            + base64.b64encode(("trial:" + env["TRIAL_AMQP_PASS"]).encode()).decode()
        )

        def broker(method, path, data=None):
            req = Request(
                api + path,
                data=None if data is None else json.dumps(data).encode(),
                method=method,
                headers={
                    "Authorization": authorization,
                    "Content-Type": "application/json",
                },
            )
            with urlopen(req, timeout=8) as response:
                return response.read()

        def ready():
            try:
                return broker("GET", "/overview")
            except OSError:
                return None

        until(ready, "RabbitMQ", 120)
        broker("PUT", "/queues/%2F/" + QUEUE, {"durable": True, "arguments": {}})

        def publish(k, n):
            result = json.loads(
                broker(
                    "POST",
                    "/exchanges/%2F/amq.default/publish",
                    {
                        "routing_key": QUEUE,
                        "payload": k,
                        "payload_encoding": "string",
                        "properties": {
                            "message_id": k,
                            "correlation_id": str(n),
                            "delivery_mode": 2,
                        },
                    },
                )
            )
            assert result["routed"]

        def snapshot(stage, ready, pending, consumers):
            expected = [QUEUE, str(ready), str(pending), str(consumers)]

            def matches():
                output = d(
                    "exec",
                    name,
                    "rabbitmqctl",
                    "-q",
                    "list_queues",
                    "name",
                    "messages_ready",
                    "messages_unacknowledged",
                    "consumers",
                )
                return any(line.split() == expected for line in output.splitlines())

            until(matches, stage)
            summary["broker"].append([stage, ready, pending, consumers])

        addr = start(1)
        snapshot("before B", 0, 0, 1)
        with urlopen(
            Request(
                addr + "/hold", data=b"hold", headers={"Content-Type": "text/plain"}
            ),
            timeout=10,
        ) as res:
            assert res.status == 202
        publish("B", 23)
        publish("C", 3)
        until(lambda: "Trial.Delivery[0] RESULT " in read(), "B result")
        snapshot("B committed, C waiting", 1, 1, 1)
        assert rows("SELECT item,units FROM effects") == [("B", 23)]
        assert len(rows("SELECT * FROM requests")) == 1
        stop()
        s1 = read()
        first = deliveries(s1)
        assert (
            len(first) == 1 and first[0][0::2] == ("B", "first") and acks(s1) == []
        ), first
        snapshot("after Stop", 2, 0, 0)
        until(lambda: json.loads(broker("GET", "/connections")) == [], "first connection to close")
        start(2)
        until(lambda: len(acks(read())) >= 2, "both ACK tokens")
        snapshot("after restart and settlement", 0, 0, 1)
        stop()
        s2 = read()
        second = deliveries(s2)
        ack = acks(s2)
        assert [x[0::2] for x in second] == [("B", "again"), ("C", "first")], second
        assert len(set([first[0][1], second[0][1], second[1][1]])) == 3
        assert ack == [second[0][1], second[1][1]], ack
        snapshot("final Stop", 0, 0, 0)
        until(lambda: json.loads(broker("GET", "/connections")) == [], "final connection to close")
        requests = rows("SELECT key,receipt,replay FROM requests ORDER BY seq")
        effects = rows("SELECT item,units,receipt FROM effects ORDER BY receipt")
        assert [r[0::2] for r in requests] == [("B", 0), ("B", 1), ("C", 0)], requests
        assert [e[:2] for e in effects] == [("B", 23), ("C", 3)], effects
        receipt = requests[0][1]
        assert receipt == requests[1][1] == effects[0][2]
        assert replies(s1) == [{"item": "B", "receipt": receipt}]
        assert replies(s2) == [
            {"item": "B", "receipt": receipt},
            {"item": "C", "receipt": effects[1][2]},
        ]
        summary.update(
            requests=requests,
            effects=effects,
            firstDelivery=first,
            secondDeliveries=second,
            secondAcks=ack,
            replies=[replies(s1), replies(s2)],
            passed=True,
        )
        (evidence / "result.json").write_text(json.dumps(summary, indent=2))
        print(json.dumps(summary), flush=True)
    finally:
        if proc and proc.poll() is None:
            proc.kill()
            proc.wait()
        if log:
            log.close()
        if container:
            d("rm", "-f", "-v", name)
        http.shutdown()
        http.server_close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--windows-control-stop", action="store_true", help="Run without stdin and stop via a Windows console control signal")
    parser.add_argument("targets", nargs="*", type=int, default=[10, 11])
    args = parser.parse_args()
    if args.windows_control_stop and os.name != "nt":
        parser.error("--windows-control-stop requires Windows")
    for major in args.targets:
        trial(Path(__file__).resolve().parent, major, args.windows_control_stop)
