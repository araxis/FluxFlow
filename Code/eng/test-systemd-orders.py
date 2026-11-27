"""Check compiled Orders under the system-wide systemd manager on a Linux test host."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time


def command(*args):
    return subprocess.check_output(args, text=True).strip()


def property_value(unit, key):
    return command("systemctl", "show", unit, "--property", key, "--value")


def verify(executable, configuration, runtime):
    with tempfile.TemporaryDirectory(prefix="fluxflow-orders-", dir="/run") as temp:
        work = Path(temp)
        work.chmod(0o755)
        unit = work.name + ".service"
        registration = Path("/run/systemd/system") / unit
        app = work / "Orders"
        shutil.copyfile(executable, app)
        app.chmod(0o755)
        config = work / "application.json"
        config.write_bytes(configuration.read_bytes())
        original = hashlib.sha256(app.read_bytes()).digest()
        # Unit paths below are controlled by the trial. A symlink avoids embedding
        # arbitrary runtime path characters in systemd syntax.
        (work / "runtime").symlink_to(runtime.resolve(), target_is_directory=True)
        with registration.open("x") as stream:
            stream.write(f"""[Service]
Type=exec
DynamicUser=yes
WorkingDirectory={work}
Environment=DOTNET_ROOT_X64={work}/runtime
ExecStart={app}

[Install]
WantedBy=multi-user.target
""")
        try:
            command("systemctl", "daemon-reload")
            assert property_value(unit, "UnitFileState") == "disabled"
            command("systemctl", "enable", "--runtime", unit)
            assert property_value(unit, "UnitFileState") == "enabled-runtime"
            previous = ""
            for attempt in range(2):
                command("systemctl", "start", unit)
                invocation = property_value(unit, "InvocationID")
                assert invocation and invocation != previous
                previous = invocation
                deadline = time.monotonic() + 30
                while True:
                    log = command("journalctl", "--no-pager", "-o", "cat", "_SYSTEMD_INVOCATION_ID=" + invocation)
                    if "1199 cents" in log:
                        break
                    if time.monotonic() >= deadline or property_value(unit, "ActiveState") != "active":
                        raise RuntimeError(f"Orders failed to produce its records:\n{log}")
                    time.sleep(.15)
                command("systemctl", "stop", unit)
                assert property_value(unit, "ActiveState") == "inactive"
                assert property_value(unit, "Result") == "success"
                assert property_value(unit, "ExecMainStatus") == "0"
                log = command("journalctl", "--no-pager", "-o", "cat", "_SYSTEMD_INVOCATION_ID=" + invocation)
                assert log.count("Order B-23 total 2950 cents") == log.count("Order C-42 total 1199 cents") == 1, log
                print(f"{executable.name}, run {attempt+1}: both orders once, clean Stop.", flush=True)
        finally:
            try:
                command("systemctl", "stop", unit)
                command("systemctl", "disable", "--runtime", unit)
            finally:
                registration.unlink()
                command("systemctl", "daemon-reload")
        assert property_value(unit, "LoadState") == "not-found"
        assert hashlib.sha256(app.read_bytes()).digest() == original
        assert config.read_bytes() == configuration.read_bytes()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executable", type=Path)
    parser.add_argument("configuration", type=Path)
    parser.add_argument("runtime", type=Path, help="Readable .NET installation for this executable")
    args = parser.parse_args()
    if os.geteuid() != 0:
        parser.error("Use an elevated Linux terminal on the test host.")
    verify(args.executable, args.configuration, args.runtime)
