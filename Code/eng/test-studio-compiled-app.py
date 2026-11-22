"""Publish and run an unsaved Studio workflow on Windows using the repository's .NET 11 SDK."""
import argparse
import uuid
from contextlib import ExitStack, contextmanager
from compiled_broker_relay import amqp_confirm_relay, drop_publish_confirmation, nats_fault_relay
from http.client import HTTPConnection
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import secrets
from threading import Event, Thread
from urllib.parse import parse_qs, urlsplit
from urllib.error import HTTPError
from urllib.request import Request, urlopen
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import tempfile
import time
from xml.etree import ElementTree as ET
from zipfile import ZipFile


ENG = Path(__file__).resolve().parent
EXPECTED = [["Information", "Unsaved.Orders", f"Order {order} total {cents} cents"]
            for order, cents in (("B-23", 2950), ("C-42", 1199))]


def generate(work, distribution, *trial, webroot=None, temp_length=95, host_env=None):
    log = work / "studio.log"
    scratch = work / "companion-temp".ljust(temp_length - len(str(work)) - 1, "-")
    scratch.mkdir(exist_ok=True)
    env = dict(os.environ, TEMP=str(scratch), TMP=str(scratch), FLUXFLOW_PLUGIN_DIRECTORY=str(work / "plugins"))
    env.update(host_env or {})
    with log.open("wb") as stream:
        host = subprocess.Popen(
            ["dotnet", "FluxFlow.StudioWebHost.dll", "--urls", "http://127.0.0.1:0",
             "--webroot", str(webroot or distribution / "site/wwwroot")],
            cwd=distribution, env=env, stdout=stream, stderr=subprocess.STDOUT)
    try:
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline and host.poll() is None:
            match = re.search(rb"Now listening on: (http://127\.0\.0\.1:\d+)", log.read_bytes())
            if match:
                if trial == ("inventory",):
                    check_catalog(match[1].decode(), work / "app-generator/feed")
                    break
                subprocess.run(["dotnet", "run", "--file", ENG / "compiled-app-test/Generate.cs",
                                "--", work, match[1].decode(), *trial], cwd=ENG, check=True, timeout=420)
                leftover = list(scratch.iterdir())
                local_temp = Path(os.environ["LOCALAPPDATA"]) / "Temp"
                leftover.extend(local_temp.glob(f"ff-{host.pid}-*"))
                if leftover:
                    raise RuntimeError(f"Publish left temporary workspaces: {leftover}")
                break
            time.sleep(.1)
        else:
            raise RuntimeError("Studio failed to start:\n" + log.read_text(errors="replace"))
    except Exception:
        with tempfile.NamedTemporaryFile(prefix="studio-publish-failure-", suffix=".log", delete=False) as report:
            with log.open("rb") as source:
                shutil.copyfileobj(source, report)
        print(f"Studio diagnostics for {trial}: {report.name}", flush=True)
        print(log.read_text(encoding="utf-8", errors="replace"), flush=True)
        raise
    finally:
        if host.poll() is None:
            host.terminate()
        host.wait(timeout=15)


def check_catalog(endpoint, feed):
    request = Request(endpoint + "/api/runs/plugins", headers={"X-FluxFlow-Run": "1"})
    with urlopen(request, timeout=15) as response:
        catalog = json.load(response)
    declared = {c["Palette"]["ComponentType"] for family in catalog for c in family["Components"]}
    resources = {r["Type"] for family in catalog for r in family["Connections"]}
    mapped, mapped_resources = set(), set()
    for package in feed.glob("*.nupkg"):
        with ZipFile(package) as archive:
            if "fluxflow/csharp-mapping.json" in archive.namelist():
                entries = json.loads(archive.read("fluxflow/csharp-mapping.json"))
                if isinstance(entries, dict):
                    entries = [entries]
                for mapping in entries:
                    target = mapped_resources if mapping.get("resource") else mapped
                    if mapping["type"] in target:
                        raise ValueError(f"{package.name} duplicates mapping {mapping['type']}.")
                    target.add(mapping["type"])
    fixtures = set()
    for fixture in (ENG / "compiled-app-test/cases").glob("*.json"):
        workflows = json.loads(fixture.read_bytes())["configuration"]["Workflows"]
        fixtures.update(node["Type"] for nodes in workflows.values() for name, node in nodes.items()
                        if name.lower() != "resources")
    matrix = set(re.findall(r"^\| `([\w.]+)` \|", (ENG / "compiled-component-coverage.md").read_text(encoding="utf-8"), re.MULTILINE))
    for label, expected, actual in (("component mappings", declared, mapped), ("resource mappings", resources, mapped_resources),
                                    ("coverage rows", declared, matrix), ("fixture components", declared, fixtures)):
        if expected != actual:
            raise RuntimeError(f"Catalog differences in {label}: {sorted(expected ^ actual)}.")
    print(f"{len(declared)} components, {len(resources)} resources and {len(catalog)} installed families match the packed "
          "mappings. Every component has a fixture and a coverage row.")


def check_reinstall(work, owners, webroot):
    installer = ["pwsh", "-NoProfile", "-File", work / "fluxflow-plugin.ps1", "-Directory", work / "plugins"]
    reopen = ["dotnet", "run", "--file", ENG / "compiled-app-test/Generate.cs", "--", work, "reopen"]
    for owner in owners:
        package, = (work / "bundled-plugins").glob(owner + ".[0-9]*.nupkg")
        subprocess.run([*installer, "-Action", "Remove", "-Id", owner], check=True, timeout=60)
        try:
            generate(work, work, "removed", owner, webroot=webroot)
        finally:
            subprocess.run([*installer, "-Action", "Install", "-Source", package], check=True, timeout=60)
        subprocess.run([*reopen, "reinstalled", owner], cwd=ENG, check=True, timeout=90)
        print(f"{owner}: removal, save/reopen and reinstall preserved the export.")


def stage_packages(feed, distribution, runtime_feed):
    # Take mappings from Studio when checking a separately supplied runtime feed.
    staged = {}
    for folder, mappings in ((runtime_feed, False), (distribution / "app-generator/feed", True)):
        for path in folder.glob("*.nupkg"):
            with ZipFile(path) as archive:
                manifest, = [name for name in archive.namelist() if "/" not in name and name.endswith(".nuspec")]
                metadata = ET.fromstring(archive.read(manifest)).find("{*}metadata")
                package = metadata.findtext("{*}id")
                version = metadata.findtext("{*}version")
                if not package.startswith("FluxFlow.") or ("fluxflow/csharp-mapping.json" in archive.namelist()) != mappings:
                    continue
            target = feed / f"{package}.{version}.nupkg"
            if target.parent != feed:
                raise ValueError(f"Invalid NuGet ID or version in {path}")
            shutil.copyfile(path, target)
            staged.setdefault(package, []).append(target)
    return staged


def publish(directory, feed, dependencies, missing=None):
    directory.mkdir()
    shutil.copyfile(directory.parent / "App.cs", directory / "App.cs")
    shutil.copyfile(ENG.parents[1] / "global.json", directory / "global.json")
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    mapping = ET.SubElement(config, "packageSourceMapping")
    for key, source, pattern in (("families", str(feed), "FluxFlow.*"), ("dependencies", dependencies, "*")):
        ET.SubElement(sources, "add", key=key, value=source)
        ET.SubElement(ET.SubElement(mapping, "packageSource", key=key), "package", pattern=pattern)
    ET.ElementTree(config).write(directory / "NuGet.Config", encoding="utf-8")
    env = dict(os.environ, NUGET_PACKAGES=str(directory / "packages"), NUGET_HTTP_CACHE_PATH=str(directory / "http"),
               DOTNET_CLI_HOME=str(directory / "cli"), DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    out = directory / "out"
    result = subprocess.run(
        ["dotnet", "publish", "App.cs", "-c", "Release", "-r", "win-x64", "--no-self-contained",
         "--configfile", "NuGet.Config", "-o", out], cwd=directory, env=env, timeout=300,
        capture_output=True, text=True, errors="replace")
    log = result.stdout + result.stderr
    if missing:
        if result.returncode == 0 or not re.search(r"error NU1101:.*\b" + re.escape(missing) + r"\.(?:\s|$)", log):
            raise RuntimeError("Expected restore to identify the missing runtime package:\n" + log)
        if list(out.glob("*")):
            raise RuntimeError(f"Failed publish left artifacts in {out}")
        print(f"Missing package: NU1101 named {missing}; no output.")
        return
    if result.returncode or [p.name for p in out.iterdir()] != ["App.exe"]:
        raise RuntimeError("Expected a single App.exe:\n" + log)
    exe = out / "App.exe"
    print(f"Published App.exe ({exe.stat().st_size:,} bytes) from an empty package cache.")
    return exe


def isolated_net10(work, aspnet=False):
    installed = Path(shutil.which("dotnet")).resolve().parent
    root = work / "dotnet10"
    root.mkdir()
    shutil.copyfile(installed / "dotnet.exe", root / "dotnet.exe")
    frameworks = ["Microsoft.NETCore.App"] + (["Microsoft.AspNetCore.App"] if aspnet else [])
    for folder in [Path("host/fxr"), *(Path("shared") / framework for framework in frameworks)]:
        versions = [v for v in (installed / folder).iterdir() if re.fullmatch(r"10\.0\.\d+", v.name)]
        if not versions:
            raise RuntimeError(f"The Windows x64 .NET 10 runtime is required ({installed / folder}).")
        version = max(versions, key=lambda v: int(v.name.split(".")[-1]))
        shutil.copytree(version, root / folder / version.name)
    env = dict(os.environ, DOTNET_ROOT_X64=str(root), DOTNET_ROOT=str(root), DOTNET_MULTILEVEL_LOOKUP="0",
               DOTNET_ROLL_FORWARD="LatestPatch", DOTNET_DISABLE_GUI_ERRORS="1")
    runtimes = subprocess.check_output([root / "dotnet.exe", "--list-runtimes"], env=env, text=True).splitlines()
    if sorted(line.split(" [")[0].split()[0] for line in runtimes) != sorted(frameworks) or any(" 10.0." not in line for line in runtimes):
        raise RuntimeError(f"Unexpected runtimes in isolated installation: {runtimes}")
    print("Isolated runtimes: " + ", ".join(runtimes))
    return env


class PendingSlot:
    def __init__(self):
        self.first_received = Event()
        self.replacement_admitted = Event()

    def exercise(self, address):
        url = urlsplit(address)
        if url.scheme != "http" or url.hostname != "127.0.0.1" or not url.port:
            raise ValueError("Expected the local test listener.")
        self.first_received.clear()
        self.replacement_admitted.clear()
        abandoned = HTTPConnection(url.hostname, url.port, timeout=10)
        try:
            abandoned.request("POST", "/call", body=b"abandoned")
            if not self.first_received.wait(5):
                raise TimeoutError("The workflow did not dispatch the first request.")
            if self.call(address, "overflow")[0] != 503:
                raise RuntimeError("The occupied reply slot should return 503.")
        finally:
            abandoned.close()

        # Admission can briefly race the server observing the socket close.
        deadline = time.monotonic() + 8
        while time.monotonic() < deadline:
            status, body = self.call(address, "replacement")
            if status != 503:
                if status != 200 or body != "replacement":
                    raise RuntimeError(f"The replacement caller received {status} {body!r}.")
                return body
            time.sleep(.025)
        raise TimeoutError("The disconnected caller did not release its slot.")

    @staticmethod
    def call(address, body):
        try:
            response = urlopen(Request(address.rstrip("/") + "/call", data=body.encode()), timeout=10)
        except HTTPError as error:
            response = error
        with response:
            return response.status, response.read().decode()


class OAuthTokenGate:
    def __init__(self):
        self.requested = Event()
        self.release = Event()
        self.access_token = secrets.token_hex(24)

    def serve(self, request, body, secret, bodies):
        response = body
        status = 200
        content_type = "text/plain"
        if request.path == "/token":
            form = parse_qs(body)
            if (form.get("client_id") != ["id-" + secret] or form.get("client_secret") != [secret]
                    or form.get("grant_type") != ["client_credentials"]):
                request.send_error(401)
                return
            token = self.access_token
            self.requested.set()
            if not self.release.wait(10):
                request.send_error(504)
                return
            content_type = "application/json"
            response = json.dumps({"access_token": token, "token_type": "Bearer", "expires_in": 3600})
        elif request.path == "/release" and self.requested.is_set() and body == "node.processing_failed":
            self.release.set()
        elif request.path == "/orders" and request.headers.get("Authorization") == "Bearer " + self.access_token:
            bodies.append(body)
            self.requested.clear()
            self.release.clear()
            self.access_token = secrets.token_hex(24)
        else:
            status = 403
        request.send_response(status)
        request.send_header("Content-Type", content_type)
        request.end_headers()
        try:
            request.wfile.write(response.encode())
        except ConnectionError:
            if request.path != "/token":
                raise


class HttpPeer:
    def __init__(self, collector_messages=(), pending_slot=False, shared_oauth=False):
        self.oauth = OAuthTokenGate() if shared_oauth else None
        self.pending = PendingSlot() if pending_slot else None
        self.collector_messages = collector_messages
        self.delete_trial_queue = None
        self.previous_delivery = None
        self.current_delivery = None
        self.bodies = []
        self.token = secrets.token_hex(24)
        peer = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def do_POST(self):
                data = self.rfile.read(int(self.headers["Content-Length"]))
                if self.path == "/v1/logs":
                    # The local collector acknowledges an ExportLogsServiceRequest with an empty protobuf response.
                    valid = (self.headers.get("X-Trial") == peer.token and
                             self.headers.get_content_type() == "application/x-protobuf" and
                             b"Orders.Collector" in data and b"OrderWorkflow" in data)
                    messages = [message for message in peer.collector_messages if message.encode() in data]
                    peer.bodies.extend(messages)
                    self.send_response(200 if valid and len(messages) == 1 else 400)
                    self.send_header("Content-Type", "application/x-protobuf")
                    self.send_header("Content-Length", "0")
                    self.end_headers()
                    return
                body = data.decode()
                if peer.oauth:
                    peer.oauth.serve(self, body, peer.token, peer.bodies)
                    return
                authorized = self.headers.get("Authorization") == "Bearer " + peer.token
                if authorized and self.path == "/delete-trial-queue" and peer.delete_trial_queue:
                    peer.bodies.append(body)
                    peer.delete_trial_queue()
                    self.send_response(204)
                    self.end_headers()
                    return
                if authorized and self.path.startswith("/token/"):
                    if self.path == "/token/remember" and body and body != peer.current_delivery:
                        # The first run uses an unknown token. Later runs use the preceding run's real token.
                        peer.previous_delivery = peer.current_delivery or "not-issued"
                        peer.current_delivery = body
                        peer.bodies.append("remember")
                        reply = peer.previous_delivery
                    elif self.path in ("/token/previous", "/token/current") and peer.current_delivery:
                        peer.bodies.append(body)
                        reply = peer.previous_delivery if self.path.endswith("/previous") else peer.current_delivery
                    else:
                        self.send_error(409)
                        return
                    self.send_response(200)
                    self.end_headers()
                    self.wfile.write(reply.encode())
                    return
                if peer.pending and self.path == "/exercise-slot":
                    try:
                        result = peer.pending.exercise(body)
                    except (OSError, ValueError, RuntimeError) as error:
                        self.send_error(500, str(error))
                        return
                    self.send_response(200)
                    self.end_headers()
                    self.wfile.write(result.encode())
                    return
                if peer.pending and authorized:
                    if self.path == "/admitted" and body == "replacement":
                        peer.pending.replacement_admitted.set()
                        self.send_response(204)
                        self.end_headers()
                        return
                    if self.path == "/orders" and body == "abandoned":
                        peer.pending.first_received.set()
                        if not peer.pending.replacement_admitted.wait(10):
                            self.send_error(504, "The replacement was never admitted")
                            return
                peer.bodies.append(body)
                if authorized and self.path == "/orders" and body == "drop-response":
                    self.close_connection = True
                    return
                status = (503 if body == "B" else 200) if authorized and self.path == "/orders" else 403
                self.send_response(status)
                self.send_header("Content-Type", "text/plain")
                self.end_headers()
                self.wfile.write(body.encode())

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.url = f"http://127.0.0.1:{self.server.server_port}"
        self.thread = Thread(target=self.server.serve_forever)
        self.thread.start()

    def close(self):
        self.server.shutdown()
        self.thread.join()
        self.server.server_close()

    def check(self, expected):
        if self.bodies != expected:
            raise RuntimeError(f"Expected downstream requests {expected}, received {self.bodies}")
        self.bodies.clear()


def exercise_listener(base_url, calls):
    for call in calls:
        request = Request(call.get("driver", base_url) + call["path"],
                          data=(base_url if call.get("driver") else call["body"]).encode(),
                          headers={"Content-Type": "text/plain; charset=utf-8"})
        try:
            response = urlopen(request, timeout=10)
        except HTTPError as error:
            response = error
        with response:
            result = response.status, response.read().decode()
        if result != (call["status"], call["response"]):
            raise RuntimeError(f"Unexpected response for {call['path']}: {result}")


def nonexistent_com_port():
    import ctypes
    query = ctypes.WinDLL("kernel32", use_last_error=True).QueryDosDeviceW
    buffer = ctypes.create_unicode_buffer(32768)
    for number in range(65000, 65100):
        name = f"COM{number}"
        if not query(name, buffer, len(buffer)) and ctypes.get_last_error() == 2:
            return name
    raise RuntimeError("Could not verify an unused port for the missing-device check.")


@contextmanager
def broker_container(image, port, *arguments, mount=None):
    volume = ["--mount", f"type=bind,source={mount},target=/trial,readonly"] if mount else []
    container = subprocess.check_output(["docker", "run", "--rm", "-d", "-p", f"127.0.0.1::{port}", *volume, image, *arguments],
                                        text=True, timeout=180).strip()
    try:
        binding = subprocess.check_output(["docker", "port", container, f"{port}/tcp"], text=True, timeout=10).strip()
        yield container, binding
    finally:
        subprocess.run(["docker", "rm", "-f", container], check=True, stdout=subprocess.DEVNULL, timeout=30)


@contextmanager
def local_rabbitmq():
    with broker_container("rabbitmq:4.1.0-management", 5672) as (container, binding):
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            try:
                with socket.create_connection(("127.0.0.1", int(binding.rsplit(":", 1)[1])), timeout=1) as amqp:
                    amqp.sendall(b"AMQP\x00\x00\x09\x01")
                    ready = amqp.recv(1) == b"\x01"
            except OSError:
                ready = False
            if ready:
                yield "amqp://" + binding, container
                return
            time.sleep(.25)
        raise RuntimeError("The temporary RabbitMQ listener did not start.")


def tls_certificates(folder, *, encrypted_key=True):
    openssl = shutil.which("openssl") or Path(os.environ["ProgramFiles"]) / "Git/usr/bin/openssl.exe"
    password = secrets.token_hex(24)
    config = folder / "openssl.cnf"
    config.write_text("[req]\ndistinguished_name=subject\n[subject]\n", encoding="ascii")
    def run(*args):
        result = subprocess.run([openssl, *args], cwd=folder,
                                env=dict(os.environ, TRIAL_PEM_PASSWORD=password, OPENSSL_CONF=str(config)),
                                capture_output=True, timeout=30)
        if result.returncode:
            raise RuntimeError(f"Creating temporary broker certificates failed: {result.stderr.decode(errors='replace')}")

    run("req", "-x509", "-nodes", "-newkey", "rsa:2048", "-keyout", "ca.key", "-out", "ca.pem",
        "-subj", "/CN=FluxFlow Trial CA", "-days", "1", "-addext", "basicConstraints=critical,CA:TRUE")
    for role, usage in (("server", "serverAuth"), ("client", "clientAuth")):
        key_options = ["-nodes"] if role == "server" or not encrypted_key else ["-passout", "env:TRIAL_PEM_PASSWORD"]
        run("req", "-new", *key_options, "-newkey", "rsa:2048", "-keyout", role + ".key", "-out",
            role + ".csr", "-subj", "/CN=" + role)
        extensions = f"basicConstraints=CA:FALSE\nextendedKeyUsage={usage}\n"
        if role == "server":
            extensions += "subjectAltName=IP:127.0.0.1\n"
        (folder / "extensions.cnf").write_text(extensions, encoding="ascii")
        run("x509", "-req", "-in", role + ".csr", "-CA", "ca.pem", "-CAkey", "ca.key", "-CAcreateserial",
            "-days", "1", "-extfile", "extensions.cnf", "-out", role + ".pem")
    return password


@contextmanager
def authenticated_nats(folder):
    folder.mkdir()
    tls_certificates(folder, encrypted_key=False)
    server = folder / "server"
    server.mkdir()
    for filename in ("server.pem", "server.key", "ca.pem"):
        shutil.copyfile(folder / filename, server / filename)
    token = secrets.token_hex(24)
    (server / "nats.conf").write_text(
        'tls { cert_file: "/trial/server.pem", key_file: "/trial/server.key", ca_file: "/trial/ca.pem", verify: true }\n'
        + f'authorization {{ token: "{token}" }}\n', encoding="ascii")
    with broker_container("nats:2.11.8-alpine", 4222, "-c", "/trial/nats.conf", mount=server) as (_, binding):
        yield binding, token


@contextmanager
def local_mosquitto(work, tls=False):
    work.mkdir()
    executable = shutil.which("mosquitto") or Path(os.environ["ProgramFiles"]) / "mosquitto/mosquitto.exe"
    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", 0))
        port = reservation.getsockname()[1]
    config = work / "mosquitto.conf"
    settings = f"listener {port} 127.0.0.1\nallow_anonymous true\npersistence false\n"
    password = None
    if tls:
        password = tls_certificates(work)
        settings += "cafile ca.pem\ncertfile server.pem\nkeyfile server.key\nrequire_certificate true\n"
    config.write_text(settings, encoding="ascii")
    log = work / "mosquitto.log"
    with log.open("wb") as stream:
        broker = subprocess.Popen([executable, "-c", config, "-v"], cwd=work, stdout=stream, stderr=subprocess.STDOUT)
    try:
        deadline = time.monotonic() + 10
        while broker.poll() is None and time.monotonic() < deadline:
            try:
                with socket.create_connection(("127.0.0.1", port), timeout=.2):
                    break
            except OSError:
                time.sleep(.05)
        else:
            raise RuntimeError(f"Mosquitto failed to listen: {log.read_text(errors='replace')}")
        yield port, log, password
    finally:
        if broker.poll() is None:
            broker.terminate()
        broker.wait(timeout=10)


def matching_records(actual, expected, ordered):
    return actual == expected if ordered else sorted(actual) == sorted(expected)


def launch(exe, log, env=None, expected=EXPECTED, calls=None, stop_after=None, ordered=True, runtime_failure=None):
    category = expected[0][1]
    levels = {"trce": "Trace", "dbug": "Debug", "info": "Information", "warn": "Warning",
              "fail": "Error", "crit": "Critical"}
    pattern = re.compile(rf"^({'|'.join(levels)}): {re.escape(category)}\[0\] ([^\r\n]*)\r?$", re.MULTILINE)
    with log.open("wb") as stream:
        app = subprocess.Popen([exe], cwd=exe.parent, stdin=subprocess.PIPE, stdout=stream, stderr=subprocess.STDOUT, env=env)
    try:
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline and app.poll() is None:
            captured = log.read_bytes()
            if calls and b"Application started." in captured and (match := re.search(rb"Now listening on: (http://127\.0\.0\.1:\d+)", captured)):
                exercise_listener(match[1].decode(), calls)
                calls = None
            if not runtime_failure and len(pattern.findall(captured.decode("utf-8", errors="replace"))) >= (stop_after or len(expected)):
                break
            time.sleep(.05)
        if runtime_failure:
            app.wait(timeout=5)
        else:
            # Allow the native GATT Stop deadline (10 seconds) to report its original error.
            app.communicate(input=b"\n", timeout=30)
    finally:
        if app.poll() is None:
            app.kill()
            app.wait()
    output = log.read_text(encoding="utf-8", errors="replace")
    records = [[levels[level], category, message] for level, message in pattern.findall(output)]
    exit_ok = (app.returncode != 0 and runtime_failure.lower() in output.lower()) if runtime_failure else app.returncode == 0
    if not exit_ok or not matching_records(records, expected, ordered):
        raise RuntimeError(f"Exit {app.returncode}, unexpected records:\n{output}")
    print(f"{log.stem}: {records!a}; exit {app.returncode}.")
    return records


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("distribution", type=Path)
    parser.add_argument("--serial-echo-port", help="Dedicated COM port connected to a 115200 baud byte echo peer")
    parser.add_argument("--ble-peer", help="12-digit public Bluetooth address running the dedicated GATT echo peer")
    parser.add_argument("--ble-server-peer", action="store_true", help="Run the GATT Server trial with its dedicated central peer")
    parser.add_argument("runtime_feed", nargs="?", type=Path, help="Run the independent App.cs trial against this runtime feed")
    parser.add_argument("--dependencies", default="https://api.nuget.org/v3/index.json",
                        help="NuGet source for third-party and SDK packages")
    fixtures = {p.stem: p for p in (ENG / "compiled-app-test/cases").glob("*.json")}
    fixtures["mqtt-manual-ack-tls"] = fixtures["mqtt-manual-ack"]
    fixtures["nats-echo-tls"] = fixtures["nats-echo"]
    fixtures["ble-peer-indicate"] = fixtures["ble-peer-notify"]
    fixtures["nats-subscriber-rejoin"] = fixtures["nats-publish-disconnect"]
    fixtures["nats-reply-disconnect"] = fixtures["nats-request-disconnect"]
    parser.add_argument("--case", choices=["orders", "inventory", *sorted(fixtures)], help="Run an inventory, Mapper order checks or one catalog case")
    args = parser.parse_args()
    if args.ble_peer and not re.fullmatch(r"[a-fA-F0-9]{12}", args.ble_peer):
        parser.error("--ble-peer requires a 12-digit hexadecimal public address.")
    if args.serial_echo_port and not re.fullmatch(r"COM[1-9][0-9]*", args.serial_echo_port, re.IGNORECASE):
        parser.error("--serial-echo-port must name a COM port, for example COM3.")
    if os.name != "nt":
        parser.error("The run check requires Windows x64.")
    distribution = args.distribution.resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix="studio-compiled-") as tmp, ExitStack() as cleanup:
        work = Path(tmp)
        if args.runtime_feed is None:
            # Run the companion beside its selected relative Source in a disposable copy.
            shutil.copytree(distribution, work, dirs_exist_ok=True, ignore=shutil.ignore_patterns("site"))
            webroot = distribution / "site/wwwroot"
            names = ["inline", "file"] if args.case in (None, "orders") else [args.case]
            if args.case is None:
                names.extend(sorted(fixtures))
            selected = [json.loads(fixtures[name].read_bytes()) for name in names if name in fixtures]
            family_root = ENG.parent / "src/Plugins"
            all_families = args.case in (None, "inventory")
            families = ({path.parent.name.removeprefix("FluxFlow.Plugins.") for path in family_root.glob("*/fluxflow-plugin.json")}
                        if all_families else {family for trial in selected for family in trial.get("pluginFamilies", [])})
            for family in sorted(families):
                project = family_root / ("FluxFlow.Plugins." + family)
                if any((work / "bundled-plugins").glob(project.name + ".[0-9]*.nupkg")):
                    continue
                subprocess.run(["dotnet", "pack", project, "-c", "Release", "-o", work / "bundled-plugins",
                                "-p:IncludeSymbols=false", "--verbosity", "quiet"], cwd=ENG, check=True, timeout=300)
            if all_families:
                generate(work, work, "inventory", webroot=webroot)
                if args.case == "inventory":
                    return
            runtime = isolated_net10(work, any("httpCalls" in trial for trial in selected))
            checked_owners = set()
            for name in names:
                expected = EXPECTED
                inputs = ("data/orders.json", "data/total.jsonata")
                mode = name
                peer, calls, launch_env, host_env = None, None, {}, {}
                runtime_failure = None
                stop_after = None
                ordered = True

                if name in fixtures:
                    mode = "catalog"
                    trial = json.loads(fixtures[name].read_bytes())
                    if name == "ble-server-peer" and not args.ble_server_peer:
                        if args.case == name:
                            parser.error("ble-server-peer requires a running central peer and --ble-server-peer.")
                        print(f"{name}: skipped (dedicated BLE central unspecified).")
                        continue
                    if name in ("ble-peer-notify", "ble-peer-indicate"):
                        if not args.ble_peer:
                            if args.case == name:
                                parser.error(f"{name} requires --ble-peer with a dedicated GATT echo peer.")
                            print(f"{name}: skipped (dedicated BLE peer unspecified).")
                            continue
                        trial["configuration"]["Resources"]["peer"]["Endpoint"] = f"ble://{args.ble_peer}/public"
                        if name == "ble-peer-indicate":
                            trial["edits"]["main.watch"]["SubscriptionMode"] = "Indicate"
                    if name in ("serial-frame-echo", "serial-text-echo"):
                        if not args.serial_echo_port:
                            if args.case == name:
                                parser.error(f"{name} requires --serial-echo-port with a dedicated byte echo peer.")
                            print(f"{name}: skipped (dedicated echo port unspecified).")
                            continue
                        trial["configuration"]["Workflows"]["main"]["Resources"]["port"]["PortName"] = args.serial_echo_port.upper()
                    if name == "ros-string-loopback":
                        if not os.environ.get("ROS_DISTRO"):
                            if args.case == name:
                                parser.error(f"{name} requires a sourced ROS distribution with std_msgs.")
                            print(f"{name}: skipped (ROS_DISTRO is unset).")
                            continue
                        trial = json.loads(fixtures[name].read_text().replace("{trial_id}", uuid.uuid4().hex))
                    if name == "ros-native-unavailable" and os.environ.get("ROS_DISTRO"):
                        print(f"{name}: requires a host without a sourced native ROS distribution.")
                        continue
                    if trial.get("startupFailure") == "{missing-port}":
                        trial = json.loads(fixtures[name].read_text().replace("{missing-port}", nonexistent_com_port()))
                    runtime_failure = trial.get("runtimeFailure")
                    if name in ("nats-subscriber-rejoin", "nats-reply-disconnect"):
                        nodes = trial["configuration"]["Workflows"]["main"]
                        sender, receiver, fault = ("send", "receive", "subscribe") if name == "nats-subscriber-rejoin" else ("ask", "reply", "reply")
                        nodes[sender]["Connection"], nodes[receiver]["Connection"] = "Resources.broker", "Resources.relay"
                        trial["natsFault"] = fault
                    stop_after = trial.get("stopAfter")
                    ordered = trial.get("ordered", True)
                    if trial.get("nats"):
                        if name == "nats-echo-tls":
                            certificates = work / "nats-certificates"
                            binding, auth_token = cleanup.enter_context(authenticated_nats(certificates))
                            connection = trial["configuration"]["Resources"]["broker"]
                            connection.update(TlsMode="Require", TokenEnvironmentVariable="TRIAL_NATS_TOKEN")
                            destination = work / "nats-destination"
                            destination.mkdir()
                            host_env["TRIAL_NATS_TOKEN"] = launch_env["TRIAL_NATS_TOKEN"] = auth_token
                            for field, variable, filename in (
                                    ("CaFileEnvironmentVariable", "TRIAL_NATS_CA", "ca.pem"),
                                    ("ClientCertificateEnvironmentVariable", "TRIAL_NATS_CERT", "client.pem"),
                                    ("ClientKeyEnvironmentVariable", "TRIAL_NATS_KEY", "client.key")):
                                connection[field] = variable
                                host_env[variable] = str(certificates / filename)
                                launch_env[variable] = str(destination / filename)
                                shutil.copyfile(certificates / filename, destination / filename)
                        else:
                            _, binding = cleanup.enter_context(broker_container("nats:2.11.8-alpine", 4222, "-js"))
                        trial["configuration"]["Resources"]["broker"]["Endpoint"] = "nats://" + binding
                        if mode_name := trial.get("natsFault"):
                            endpoint, wire_counts = cleanup.enter_context(nats_fault_relay(binding, mode_name))
                            trial["configuration"]["Resources"]["relay"]["Endpoint"] = endpoint
                    if trial.get("rabbitmq"):
                        endpoint, container = cleanup.enter_context(local_rabbitmq())
                        trial["configuration"]["Resources"]["broker"]["Endpoint"] = endpoint
                        if trial.get("amqpLostConfirm"):
                            port, wire_counts = cleanup.enter_context(amqp_confirm_relay(urlsplit(endpoint).port))
                            trial["configuration"]["Resources"]["publisher"]["Endpoint"] = f"amqp://127.0.0.1:{port}"
                    if trial.get("mosquitto"):
                        tls = name == "mqtt-manual-ack-tls"
                        broker_dir = work / (name + "-broker")
                        mqtt_port, mqtt_log, password = cleanup.enter_context(local_mosquitto(broker_dir, tls))
                        connection = trial["configuration"]["Resources"]["broker"]
                        connection["Port"] = mqtt_port
                        if trial.get("dropPublisherAck") or trial.get("dropSubscriberAck"):
                            sender = trial["configuration"]["Resources"]["sender"]
                            subscriber = bool(trial.get("dropSubscriberAck"))
                            relay_port = cleanup.enter_context(drop_publish_confirmation(
                                mqtt_port, subscriber=subscriber, fail_first_reconnect=trial.get("failFirstReconnect", False)))
                            connection["Port"], sender["Port"] = (relay_port, mqtt_port) if subscriber else (mqtt_port, relay_port)
                        if tls:
                            connection.update(Tls=True, CertificateRevocationMode="NoCheck",
                                              ClientCertificate={"Path": str(broker_dir / "client.pem"),
                                                                 "PrivateKeyPath": str(broker_dir / "client.key"),
                                                                 "Password": password},
                                              TrustedCertificates=[str(broker_dir / "ca.pem")])
                            credentials = work / "destination-credentials"
                            credentials.mkdir()
                            for field, filename in (("ClientCertificate__Path", "client.pem"),
                                                    ("ClientCertificate__PrivateKeyPath", "client.key"),
                                                    ("TrustedCertificates__0", "ca.pem")):
                                launch_env["Resources__broker__" + field] = str(credentials / filename)
                                shutil.copyfile(broker_dir / filename, credentials / filename)
                            launch_env["Resources__broker__ClientCertificate__Password"] = password
                            trial["secretEnvironment"] = {
                                "Resources__broker__ClientCertificate__Password": password}
                    if "downstream" in trial:
                        peer = HttpPeer(trial["downstream"] if trial.get("collector") else (), trial.get("pendingSlot", False),
                                        trial.get("sharedOAuth", False))
                        cleanup.callback(peer.close)
                        if trial.get("deleteTrialQueue"):
                            peer.delete_trial_queue = lambda: subprocess.run(
                                ["docker", "exec", container, "rabbitmqctl", "delete_queue", "trial.orders"],
                                check=True, capture_output=True, timeout=15)
                        trial = json.loads(json.dumps(trial).replace("{fixture-token}", peer.token)
                                           .replace("{fixture-url}", peer.url))
                        if trial.get("collector"):
                            launch_env["Resources__logs__Headers"] = "X-Trial=" + peer.token
                        elif peer.oauth:
                            launch_env["Resources__api__Authentication__ClientId"] = "id-" + peer.token
                            launch_env["Resources__api__Authentication__ClientSecret"] = peer.token
                        else:
                            launch_env["Resources__api__Authentication__Token"] = peer.token
                    calls = trial.get("httpCalls")
                    if peer:
                        trial["secretEnvironment"] = dict(launch_env) if not trial.get("collector") else dict.fromkeys(launch_env, peer.token)
                    (work / "trial.json").write_text(json.dumps(trial), encoding="utf-8")
                    messages = trial["messages"]
                    levels = trial.get("levels", ["Information"] * len(messages))
                    if len(levels) != len(messages):
                        raise ValueError(f"{name}: expected one log level per message.")
                    expected = [[level, trial["category"], message] for level, message in zip(levels, messages)]
                    inputs = trial["files"].keys()
                print(f"Testing {name} in Studio, .NET 10 and .NET 11.", flush=True)
                generate(work, work, mode, webroot=webroot, temp_length=150 if mode == "file" else 95, host_env=host_env)
                if peer:
                    peer.check(trial["downstream"] * 2)
                if name == "nats-echo-tls":
                    for filename in ("ca.pem", "client.pem", "client.key"):
                        (certificates / filename).unlink()
                if name == "mqtt-manual-ack-tls":
                    (broker_dir / "client.pem").unlink()
                    (broker_dir / "client.key").unlink()
                if any((work / path).exists() for path in inputs):
                    raise RuntimeError("The selected input files must be absent during launch")
                run = work / name
                run.mkdir()
                exe = (work / "App.exe").rename(run / "App.exe")
                exe10 = (work / "App-net10.exe").rename(run / "App-net10.exe")
                incompatible = subprocess.run([exe], cwd=run, env=runtime, input="\n", capture_output=True, text=True, timeout=30)
                if not incompatible.returncode or "11.0." not in incompatible.stderr or "Microsoft.NETCore.App" not in incompatible.stderr:
                    raise RuntimeError(f"Expected the .NET 11 EXE to require its missing runtime: {incompatible.stderr}")
                for attempt in (1, 2):
                    studio = json.loads((work / f"studio-{attempt}.json").read_bytes())
                    if name in fixtures and (failure := trial.get("startupFailure")):
                        for binary, environment in ((exe10, runtime), (exe, os.environ)):
                            result = subprocess.run([binary], cwd=run, env=environment, input="\n", text=True,
                                                    capture_output=True, timeout=30)
                            if result.returncode == 0 or failure.lower() not in (result.stdout + result.stderr).lower():
                                raise RuntimeError(f"Unexpected {binary.name} startup outcome: {result.stdout} {result.stderr}")
                        if studio != failure:
                            raise RuntimeError(f"Unexpected Studio startup outcome: {studio}")
                        print(f"{name}: matching startup failure on attempt {attempt}: {failure}.")
                        continue
                    net10 = launch(exe10, run / f"net10-{attempt}.log", env=dict(runtime, **launch_env), expected=expected, calls=calls, stop_after=stop_after, ordered=ordered, runtime_failure=runtime_failure)
                    if peer:
                        peer.check(trial["downstream"])
                    net11 = launch(exe, run / f"net11-{attempt}.log", env=dict(os.environ, **launch_env), expected=expected, calls=calls, stop_after=stop_after, ordered=ordered, runtime_failure=runtime_failure)
                    if peer:
                        peer.check(trial["downstream"])
                    if not matching_records(studio, net10, ordered) or not matching_records(studio, net11, ordered):
                        raise RuntimeError(f"{mode} run {attempt}: Studio {studio}, .NET 10 {net10}, .NET 11 {net11}")
                    print(f"{name}: Studio Run {attempt} matched both executables.")
                if name in fixtures and "wireCounts" in trial and wire_counts != trial["wireCounts"]:
                    raise RuntimeError(f"{name}: expected wire counts {trial['wireCounts']}, got {wire_counts}")
                if name in fixtures and trial.get("rabbitmq"):
                    queues = subprocess.check_output(["docker", "exec", container, "rabbitmqctl", "-q", "list_queues",
                                                      "name", "messages_ready", "messages_unacknowledged"], text=True, timeout=30)
                    if trial.get("deleteTrialQueue"):
                        if "trial.orders" in queues:
                            raise RuntimeError(f"RabbitMQ retained the cancelled queue: {queues}")
                    elif "trial.orders\t0\t0" not in queues:
                        raise RuntimeError(f"RabbitMQ retained unsettled deliveries: {queues}")
                if name in fixtures and trial.get("mosquitto"):
                    broker_log = mqtt_log.read_text(errors="replace")
                    acknowledgements = broker_log.count("Received PUBACK from trial-workflow")
                    expected_acks = 12 if trial.get("dropPublisherAck") else 6
                    if acknowledgements != expected_acks:
                        raise RuntimeError(f"Expected {expected_acks} subscriber PUBACKs, got {acknowledgements}.")
                    if trial.get("dropSubscriberAck"):
                        redeliveries = broker_log.count("Sending PUBLISH to trial-workflow (d1, q1")
                        if redeliveries != 6:
                            raise RuntimeError(f"Expected six broker redeliveries, got {redeliveries}.")
                    if trial.get("dropPublisherAck"):
                        for topic in ("trial/uncertain", "trial/confirmed"):
                            count = sum("Received PUBLISH from trial-publisher " in line and f"'{topic}'" in line
                                        for line in broker_log.splitlines())
                            if count != 6:
                                raise RuntimeError(f"Expected six broker receipts for {topic}, got {count}.")
                owners = set(json.loads((work / "owners.json").read_bytes()))
                check_reinstall(work, sorted(owners - checked_owners), webroot)
                checked_owners.update(owners)
                cleanup.close()
            if args.case not in (None, "orders"):
                return
            for invalid in ("invalid-engine", "invalid-file-engine"):
                generate(work, work, invalid, "'total.Engine' must be 'jsonata'", webroot=webroot)
            if args.case == "orders":
                return
            feed = work / "app-generator/feed"
            for pattern, expected in (("FluxFlow.Components.Source.[0-9]*.nupkg", "NU1101: Unable to find package FluxFlow.Components.Source."),
                                      ("FluxFlow.Components.Logging.Fluent.*.nupkg", "No C# mapping for 'main.record'")):
                package, = feed.glob(pattern)
                withheld = package.rename(package.with_suffix(".withheld"))
                generate(work, work, "compiled", expected, webroot=webroot, temp_length=150)
                withheld.rename(package)
            return
        generate(work, distribution)
        if (work / "data/orders.json").exists():
            raise RuntimeError("Source file still exists before publish")
        feed = work / "feed"
        feed.mkdir()
        packages = stage_packages(feed, distribution, args.runtime_feed.resolve(strict=True))
        exe = publish(work / "success", feed, args.dependencies)
        run = work / "run"
        run.mkdir()
        shutil.copyfile(exe, run / "App.exe")
        for attempt in (1, 2):
            launch(run / "App.exe", work / f"launch-{attempt}.log")
        for missing in ("FluxFlow.Components.Logging.Fluent", "FluxFlow.Components.Source"):
            for package in packages[missing]:
                package.rename(package.with_suffix(".withheld"))
            publish(work / missing, feed, args.dependencies, missing=missing)
            for package in packages[missing]:
                package.with_suffix(".withheld").rename(package)


if __name__ == "__main__":
    main()
