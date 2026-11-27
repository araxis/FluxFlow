"""Verify workflow-to-service deployment in an elevated Windows terminal."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import uuid


def verify(sdk, orders, bad_feed):
    import msvcrt
    from compiled_cli_publish import cancel_during_build

    with tempfile.TemporaryDirectory(prefix="fluxflow-install-") as directory:
        work = Path(directory)
        project = Path(shutil.copytree(orders, work / "Orders project"))
        name = "FluxFlow Orders " + uuid.uuid4().hex[:12]
        installed = Path(os.environ["ProgramFiles"]) / "FluxFlow/Services" / ("service-" + name)
        sc = Path(os.environ["SystemRoot"]) / "System32/sc.exe"

        def run(action, *args, code=0, input="", cwd=project):
            result = subprocess.run([sdk, "service", action, name, *args], cwd=cwd, input=input,
                                    capture_output=True, text=True, timeout=300)
            output = result.stdout + result.stderr
            assert result.returncode == code, (result.returncode, output)
            return output

        def unregister():
            if subprocess.run([sc, "query", name], capture_output=True).returncode != 1060:
                run("stop")
                subprocess.run([sc, "delete", name], check=True)
            (installed / "settings.json").unlink(missing_ok=True)
            (installed / "App.exe").unlink()
            installed.rmdir()

        assert not installed.exists()
        (project / "unrelated.json").write_text("[]")
        (project / "broken.json").write_text("{")
        assert "LineNumber" in run("install", "broken.json", code=1)
        config = project / "application.json"
        original_config = config.read_bytes()
        alternate = project / "A separate workflow.json"
        alternate.write_text(json.dumps({"Workflows": {"main": {"sink": {"Type": "logger"}}}}))
        with (project / "busy.json").open("w+b") as busy:
            busy.write(b"{}")
            busy.seek(0)
            msvcrt.locking(busy.fileno(), msvcrt.LK_NBLCK, 1)
            ambiguous = run("install", code=1)
            assert "Multiple FluxFlow configurations" in ambiguous and "Skipping 'busy.json'" in ambiguous, ambiguous
            assert config.name in ambiguous and alternate.name in ambiguous and "broken.json" not in ambiguous
            assert "busy.json" in run("install", "busy.json", code=1)
        run("install", "--interactive", input="\n", code=130)
        assert not installed.exists()
        if bad_feed:
            broken = Path(shutil.copytree(bad_feed, work / "Broken feed"))
            (broken / "NuGet.Config").write_text("<invalid")
            run("install", str(config), "--feed", str(broken), cwd=work, code=1)
            assert not installed.exists()
            assert subprocess.run([sc, "query", name], capture_output=True).returncode == 1060

        scratch = work / "scratch"
        scratch.mkdir()
        for major in (10, 11):
            cancel_during_build([sdk, "service", "install", name, str(config), "--framework", f"net{major}.0"],
                                scratch, work, dict(os.environ, TEMP=str(scratch), TMP=str(scratch)))
            assert not any(scratch.glob("ff-*")) and not installed.exists()
            assert subprocess.run([sc, "query", name], capture_output=True).returncode == 1060
            if major == 10:
                alternate.unlink()
                args = ["--framework", "net10.0"]
                selection = ""
            else:
                alternate.write_text(json.dumps({"Workflows": {"main": {"sink": {"Type": "logger"}}}}))
                args = ["--interactive", "--startup", "automatic"]
                selection = "2\n"
            run("install", *args, input=selection)
            try:
                import winreg
                with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, "SYSTEM\\CurrentControlSet\\Services\\" + name) as key:
                    assert winreg.QueryValueEx(key, "Start")[0] == (3 if major == 10 else 2)
                exe = installed / "App.exe"
                before = hashlib.sha256(exe.read_bytes()).digest()
                settings = installed / "settings.json"
                settings.write_bytes(original_config)
                assert "already registered" in run("install", str(config), cwd=work, code=1)
                assert hashlib.sha256(exe.read_bytes()).digest() == before
                withheld = project.rename(work / "withheld")
                try:
                    run("start", cwd=work)
                    for running in (True, False):
                        if not running:
                            run("install", str(exe), cwd=work)
                        run("uninstall", cwd=work)
                        assert subprocess.run([sc, "query", name], capture_output=True).returncode == 1060
                        assert hashlib.sha256(exe.read_bytes()).digest() == before
                        assert settings.read_bytes() == original_config
                        assert (withheld / "application.json").read_bytes() == original_config
                    # Check the embedded Orders data independently of SCM state.
                    import runpy
                    launch = runpy.run_path(str(Path(__file__).with_name("test-studio-compiled-app.py")))["launch"]
                    launch(exe, work / f"orders-{major}.log")
                finally:
                    withheld.rename(project)
            finally:
                unregister()
            print(f".NET {major}: workflow installed, original inputs withheld, service and Orders output verified.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("orders", type=Path, help="Directory containing the compiled Orders fixture and its relative files")
    parser.add_argument("--sdk", default="fluxflow")
    parser.add_argument("--feed", type=Path, help="Copy this runtime feed to exercise a failed SDK restore")
    args = parser.parse_args()
    if os.name != "nt" or not ctypes.windll.shell32.IsUserAnAdmin():
        parser.error("Use an elevated Windows terminal for the temporary services.")
    verify(args.sdk, args.orders.resolve(), args.feed)
