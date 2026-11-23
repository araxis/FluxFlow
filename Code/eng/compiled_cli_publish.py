"""Check the packaged publish command using the same inputs selected by Studio."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
from zipfile import ZipFile
from compiled_build_kit import build_kit


def cancel_during_build(command, scratch, cwd, env):
    # CTRL_BREAK needs a shared Windows console even when CI redirects all output.
    # Start the controller in a private console and target only the new process group.
    if not sys.stdin.isatty() and not os.environ.get("FLUXFLOW_TEST_CONSOLE"):
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        subprocess.run([sys.executable, __file__, scratch, *command], cwd=cwd,
                       env=dict(env, FLUXFLOW_TEST_CONSOLE="1"), startupinfo=startup,
                       creationflags=subprocess.CREATE_NEW_CONSOLE, check=True, timeout=90)
        return
    with (cwd / "cancel.log").open("wb") as stream:
        process = subprocess.Popen(command, cwd=cwd, env=env, stdout=stream, stderr=stream,
                                   creationflags=subprocess.CREATE_NEW_PROCESS_GROUP)
        try:
            deadline = time.monotonic() + 40
            while not any(scratch.glob("ff-*/t/*")):
                if process.poll() is not None or time.monotonic() > deadline:
                    raise RuntimeError("Publish did not reach SDK staging before cancellation")
                time.sleep(.025)
            process.send_signal(signal.CTRL_BREAK_EVENT)
            code = process.wait(timeout=30)
            if code != 130:
                raise RuntimeError(f"Expected cancellation exit code 130, got {code}")
        finally:
            if process.poll() is None:
                subprocess.run(["taskkill", "/F", "/T", "/PID", str(process.pid)], check=False)
                process.wait(timeout=10)


def publish_orders(work, output=None):
    kit = build_kit(work / "app-generator/feed", work / "cli-inputs", output or work)
    shutil.rmtree(work / "cli-inputs")
    with tempfile.TemporaryDirectory(prefix="fluxflow-kit-") as directory:
        caller = Path(directory)
        with ZipFile(kit) as archive:
            archive.extractall(caller)
        apps = check_extracted_kit(caller)
        results = {}
        for (command, major), app in apps.items():
            destination = work / f"{command}-net{major}/Orders.exe"
            destination.parent.mkdir()
            results[command, major] = Path(shutil.copyfile(app, destination))
    return results


def check_extracted_kit(caller):
    feed = caller / "feed"
    env = dict(os.environ, NUGET_PACKAGES=str(caller / "packages"), DOTNET_CLI_HOME=str(caller / "home"),
               NUGET_HTTP_CACHE_PATH=str(caller / "http-cache"),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    scratch = caller / "temp"
    scratch.mkdir()
    env.update(TEMP=str(scratch), TMP=str(scratch))
    inputs = caller / "orders"
    export = inputs / "application.json"
    launcher = str(caller / "bin/fluxflow.exe")
    # First action in a fresh kit: publish from a child directory with relative arguments.
    entry_points = {"launcher": [launcher], "cli": ["dotnet", "fluxflow"]}
    results = {}
    for major in (10, 11):
        relative_args = ["publish", export.name, "--framework", f"net{major}.0", "--feed", "../feed"]
        for name, entry in entry_points.items():
            path = Path("out") / name / f"net{major}" / "Orders app.exe"
            subprocess.run([*entry, *relative_args, "--output", Path("..") / path], cwd=inputs, env=env,
                           check=True, timeout=300)
            results[name, major] = caller / path
        linux = results["launcher", major].with_suffix("")
        subprocess.run([launcher, *relative_args, "--output", linux, "--runtime", "linux-x64"],
                       cwd=inputs, env=env, check=True, timeout=300)
        with linux.open("rb") as stream:
            header = stream.read(20)
        if header[:6] != b"\x7fELF\x02\x01" or header[18:20] != b"\x3e\x00":
            raise RuntimeError(f".NET {major} publish did not produce an x86-64 Linux executable")
        if list(scratch.glob("ff-*")):
            raise RuntimeError("Successful publish left an SDK workspace")

    # A nested project's uninstalled pin must not fall through to the restored kit CLI.
    nested = caller / "separate project"
    nested.mkdir()
    manifest = json.loads((caller / ".config/dotnet-tools.json").read_text())
    manifest["tools"]["dotnet-fluxflow"]["version"] = "0.0.0-unrestored"
    (nested / "dotnet-tools.json").write_text(json.dumps(manifest), encoding="utf-8")
    for entry in entry_points.values():
        error = subprocess.run([*entry, "--help"], cwd=nested, env=env, capture_output=True, timeout=30)
        if error.returncode == 0:
            raise RuntimeError("A project with an unrestored pin ran another CLI version")
    generated = inputs / "Orders & totals.cs"
    packages = sorted(feed.glob("*.nupkg"))
    sources = []
    for entry in entry_points.values():
        subprocess.run([*entry, "generate", export.name, generated.name, *packages], cwd=inputs, env=env,
                       check=True, timeout=60)
        sources.append(generated.read_bytes())
        generated.unlink()
    if sources[0] != sources[1]:
        raise RuntimeError("The launcher and local CLI generated different C#")

    destination = results["launcher", 11]
    publish_args = ["publish", str(export), "--framework", "net11.0", "--feed", str(feed), "--output", str(destination)]
    original = hashlib.sha256(destination.read_bytes()).digest()
    command = [launcher, *publish_args]

    def preserved():
        if (hashlib.sha256(destination.read_bytes()).digest() != original
                or list(destination.parent.glob("*.tmp")) or list(scratch.glob("ff-*"))):
            raise RuntimeError("Failed publish changed the existing EXE or left a staged output")

    def reject(expected, args=command):
        result = subprocess.run(args, cwd=caller, env=env, capture_output=True, text=True, timeout=300)
        if result.returncode == 0 or expected not in result.stderr:
            raise RuntimeError(f"Expected {expected!r}, got {result.returncode}: {result.stderr}")
        preserved()

    reject("net10.0|net11.0", [a if a != "net11.0" else "net9.0" for a in command])
    reject("win-x64|linux-x64", [*command, "--runtime", "unknown"])
    invalid = ["publish", str(export), "--runtime", "unknown"]
    errors = [subprocess.run([*entry, *invalid], cwd=caller, env=env, capture_output=True, timeout=30)
              for entry in entry_points.values()]
    if errors[0].returncode != 2 or errors[1].returncode != 2 or errors[0].stderr != errors[1].stderr:
        raise RuntimeError("The launcher and local CLI reported different argument errors")
    fixture = Path(__file__).parent / "compiled-app-test/cases/ble-server-peer.json"
    ble = caller / "ble.json"
    ble.write_text(json.dumps(json.loads(fixture.read_text())["configuration"]), encoding="utf-8")
    reject("cannot publish for linux-x64", [str(ble) if a == str(export) else a for a in command]
           + ["--runtime", "linux-x64"])
    for file, diagnostic in [(inputs / "data/orders.json", "orders.json"),
                             (next(feed.glob("FluxFlow.Components.Logging.Fluent.*.nupkg")), "No C# mapping"),
                             (next(feed.glob("FluxFlow.Components.Source.[0-9]*.nupkg")), "NU1101: Unable to find package FluxFlow.Components.Source.")]:
        withheld = file.rename(file.with_suffix(".withheld"))
        try:
            reject(diagnostic)
        finally:
            withheld.rename(file)

    # Use a short path so the SDK's long-TEMP fallback need not relocate this workspace.
    with tempfile.TemporaryDirectory(prefix="fc-") as directory:
        cancel_scratch = Path(directory)
        for entry in entry_points.values():
            cancel_during_build([*entry, *publish_args], cancel_scratch, caller, dict(env, TEMP=directory, TMP=directory))
            deadline = time.monotonic() + 10
            while list(cancel_scratch.glob("ff-*")) and time.monotonic() < deadline:
                time.sleep(.1)
            if list(cancel_scratch.glob("ff-*")):
                raise RuntimeError("Canceled publish left an SDK workspace")
            preserved()
    print("Both SDK commands passed generation, publish and cancellation checks.", flush=True)
    return results


if __name__ == "__main__":
    cancel_during_build(sys.argv[2:], Path(sys.argv[1]), Path.cwd(), os.environ)
