"""Package the published Studio feed with its pinned tool and the Orders example."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
from xml.etree import ElementTree
from zipfile import ZipFile, ZipInfo


def build_sdk(feed, directory):
    eng = Path(__file__).resolve().parent
    with tempfile.TemporaryDirectory(prefix="fluxflow-sdk-pack-") as build:
        subprocess.run(["dotnet", "pack", str(eng.parent / "src/Authoring/FluxFlow.Sdk"), "-c", "Release",
                        "-o", build, f"-p:FluxFlowSdkFeed={feed.resolve()}"], cwd=eng, check=True, timeout=180)
        package, = Path(build).glob("FluxFlow.Sdk.*.nupkg")
        directory.mkdir(parents=True, exist_ok=True)
        return Path(shutil.copyfile(package, directory / package.name))


def build_kit(feed, orders, directory):
    eng = Path(__file__).resolve().parent
    tool, = feed.glob("dotnet-fluxflow.*.nupkg")
    with ZipFile(tool) as package:
        metadata = ElementTree.fromstring(package.read("dotnet-fluxflow.nuspec")).find("{*}metadata")
        version = metadata.findtext("{*}version")
        repository = metadata.find("{*}repository")
        revision = repository.get("commit", "") if repository is not None else ""
    if not version:
        raise ValueError(f"Missing tool version in {tool.name}")
    if not re.fullmatch(r"[a-f0-9]{40}", revision):
        raise ValueError(f"Missing source revision in {tool.name}")
    manifest = {"version": 1, "isRoot": True, "tools": {
        "dotnet-fluxflow": {"version": version, "commands": ["dotnet-fluxflow"]}}}
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / f"FluxFlow-build-kit-{version}-{revision[:12]}-win-x64.zip"
    with tempfile.TemporaryDirectory(prefix="fluxflow-launcher-") as build:
        subprocess.run(["dotnet", "publish", str(eng / "sdk-launcher/fluxflow.cs"), "-c", "Release",
                        "-r", "win-x64", "--no-self-contained", "-o", build], cwd=eng, check=True, timeout=180)
        launcher = (Path(build) / "fluxflow.exe").read_bytes()
    with ZipFile(path, "w") as archive:
        # Stable entry metadata keeps the checksum tied to file contents.
        for file in sorted([feed / "NuGet.Config", *feed.glob("*.nupkg")]):
            archive.writestr(ZipInfo("feed/" + file.name), file.read_bytes())
        archive.writestr(ZipInfo("bin/fluxflow.exe"), launcher)
        for name in ("application.json", "data/orders.json", "data/total.jsonata"):
            archive.writestr(ZipInfo("orders/" + name), (orders / name).read_bytes())
        archive.writestr(ZipInfo(".config/dotnet-tools.json"), json.dumps(manifest, indent=2) + "\n")
        archive.writestr(ZipInfo("README.md"), (eng / "compiled-app-test/build-kit.md").read_bytes())
        archive.writestr(ZipInfo("revision.txt"), revision + "\n")
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    path.with_suffix(".zip.sha256").write_text(f"{digest}  {path.name}\n", encoding="ascii")
    print(f"Build kit {path.name} SHA-256 {digest}", flush=True)
    return path
