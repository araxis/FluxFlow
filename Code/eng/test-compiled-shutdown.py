"""Publish a draining HTTP workflow and check its shutdown deadline and next run."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from http.client import HTTPConnection
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import tempfile
import time


def wait_for_log(path, pattern, process):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline and process.poll() is None:
        match = re.search(pattern, path.read_text(encoding="utf-8", errors="replace"))
        if match:
            return match
        time.sleep(.05)
    raise RuntimeError(f"Missing {pattern!r} in {path.read_text(errors='replace')}")


def post(port):
    client = HTTPConnection("127.0.0.1", port, timeout=60)
    try:
        client.request("POST", "/hold", "pending", {"Content-Type": "text/plain"})
        return client.getresponse().status
    finally:
        client.close()


def check(exe, hold):
    path = exe.with_suffix(".log")
    with path.open("wb") as stream, ThreadPoolExecutor(1) as peer:
        process = subprocess.Popen([exe], cwd=exe.parent, stdin=subprocess.PIPE, stdout=stream, stderr=subprocess.STDOUT)
        try:
            port = int(wait_for_log(path, r"Now listening on: http://127\.0\.0\.1:(\d+)", process)[1])
            if hold:
                pending = peer.submit(post, port)
                wait_for_log(path, "Accepted outstanding request", process)
            started = time.monotonic()
            process.communicate(b"\n", timeout=55)
            elapsed = time.monotonic() - started
            log = path.read_text(encoding="utf-8", errors="replace")
            if hold:
                assert 25 <= elapsed < 55 and pending.result(timeout=5) == 503, log
            assert process.returncode == (2 if hold else 0), log
            assert log.count("Shutdown incomplete:") == int(hold), log
            with socket.socket() as probe:
                probe.settimeout(2)
                assert probe.connect_ex(("127.0.0.1", port)) != 0, "HTTP listener remained open"
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk", default="fluxflow", help="Installed FluxFlow SDK command")
    args = parser.parse_args()
    runtime = "win-x64" if os.name == "nt" else "linux-x64"
    with tempfile.TemporaryDirectory(prefix="fluxflow-shutdown-") as directory:
        root = Path(directory)
        config = root / "application.json"
        config.write_text(json.dumps({"Workflows": {"main": {
            "incoming": {"Type": "http.trigger", "Route": "/hold", "ResponseMode": "Reply",
                         "ReplyTimeout": "00:03:00", "ShutdownMode": "Drain", "Output": "format.Input"},
            "format": {"Type": "mapper", "Engine": "jsonata", "Output": "log.Input",
                       "Expression": "{'LogLevel':'Information','MessageTemplate':'Accepted outstanding request'}"},
            "log": {"Type": "logger", "Category": "Trial.Held"}
        }}}), encoding="utf-8")
        for major in (10, 11):
            exe = root / (f"Held{major}.exe" if os.name == "nt" else f"Held{major}")
            subprocess.run([args.sdk, "publish", config, "--framework", f"net{major}.0", "--runtime", runtime,
                            "--output", exe], cwd=root, check=True, timeout=300)
            check(exe, hold=True)
            check(exe, hold=False)
            print(f".NET {major}: incomplete Stop, listener cleanup and clean restart passed.", flush=True)
