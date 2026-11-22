"""Usage: test-generate-app.py <Source.Fluent.nupkg> <Logging.Fluent.nupkg> [dotnet-fluxflow.nupkg]"""
import base64
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import warnings
import zipfile
from xml.etree import ElementTree


def check(directory, source, logger, command, env=None):
    config = directory / "application.json"
    output = directory / "App.cs"

    def generate(nodes, packages, error=None):
        config.write_text(json.dumps({"Workflows": {"main": nodes}}), encoding="utf-8")
        output.write_bytes(b"previous output")
        run = subprocess.run(
            [*command, config, output, *packages],
            cwd=directory, env=env, capture_output=True, text=True, timeout=90,
        )
        if error:
            assert run.returncode == 1 and error in run.stderr, run.stderr
            assert output.read_bytes() == b"previous output"
            return
        assert run.returncode == 0, run.stderr
        return output.read_text(encoding="utf-8")

    log = {"Type": "logger"}
    sink = {"sink": log}
    packages = [source, logger]
    code = generate(sink, packages)
    assert "#:package FluxFlow.Components.Logging.Fluent@" in code
    assert "#:package FluxFlow.Components.Source.Fluent@" not in code
    assert "#:package FluxFlow.Components.Logging.Otlp.Fluent@" not in code
    generate({"sink": {"Type": "logger", "Logger": "Resources.missing"}}, packages,
             "requires a logging.otlp resource in scope")
    generate(sink, [source], "No C# mapping")
    generate(sink, [logger, logger], "Select one runtime package")

    samples = {"Type": "source", "Values": "[]", "Output": "sink.Input"}
    generate({"invalid.name": samples, **sink}, packages, "address segment")
    for target in ("missing.Input", "sink.Output", "sink.Events", "sink.Input.extra"):
        generate({"samples": {**samples, "Output": target}, **sink}, packages, "Unknown input")

    text = ' \t[\r\n  "é雪"\r\n] \r\n'
    data = b"\xef\xbb\xbf" + text.encode() + b" " * 70000
    (directory / "values.json").write_bytes(data)
    for value, expected in ((text, text.encode()), ("inline:" + text, text.encode()), ("file:values.json", data)):
        code = generate({"samples": {**samples, "Values": value}, **sink}, packages)
        literal = re.search(r'FromBase64String\(("[^"]*")\)', code)
        assert literal and base64.b64decode(json.loads(literal[1]), validate=True) == expected

    # A package with two declarations at the same path must not silently use the first.
    duplicate = directory / "duplicate.nupkg"
    shutil.copyfile(logger, duplicate)
    entry = "fluxflow/csharp-mapping.json"
    with zipfile.ZipFile(duplicate, "a") as archive, warnings.catch_warnings():
        warnings.filterwarnings("ignore", message="Duplicate name:")
        archive.writestr(entry, archive.read(entry))
    generate(sink, [duplicate], "Multiple C# mappings")

    # An invalid alternative must be rejected even when the current graph uses the default method.
    malformed = directory / "malformed.nupkg"
    with zipfile.ZipFile(logger) as original, zipfile.ZipFile(malformed, "w") as archive:
        for item in original.infolist():
            contents = original.read(item)
            if item.filename == entry:
                declaration = json.loads(contents)
                declaration["alternatives"] = [{"whenPresent": "Mode", "method": "Alternate",
                                                "arguments": [{"name": "Mode", "kind": "flags"}]}]
                contents = json.dumps(declaration).encode()
            archive.writestr(item, contents)
    generate(sink, [malformed], "Invalid C# mapping")

    # Different versions of one package can declare different component types.
    alternate = directory / "alternate.nupkg"
    with zipfile.ZipFile(source) as src, zipfile.ZipFile(logger) as log_pkg, zipfile.ZipFile(alternate, "w") as archive:
        spec_name = next(name for name in src.namelist() if name.endswith(".nuspec"))
        spec = ElementTree.fromstring(src.read(spec_name))
        spec.find("{*}metadata/{*}version").text = "0.0.0-review"
        archive.writestr(spec_name, ElementTree.tostring(spec))
        archive.writestr(entry, log_pkg.read(entry))
    generate({"samples": samples, **sink}, [source, alternate], "Conflicting versions")
    generate({"samples": {**samples, "Values": "file:values.json"}, **sink}, packages)
    return output.read_bytes()


def tool_environment(directory):
    return dict(os.environ, DOTNET_CLI_HOME=str(directory / "cli"), NUGET_PACKAGES=str(directory / "packages"),
                DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")


def check_local_tool(work, source, logger, tool):
    feed = work / "feed"
    feed.mkdir()
    with zipfile.ZipFile(tool) as archive:
        spec = ElementTree.fromstring(archive.read("dotnet-fluxflow.nuspec"))
        version = spec.findtext("{*}metadata/{*}version")
        for major in (10, 11):
            settings = archive.read(f"tools/net{major}.0/any/DotnetToolSettings.xml")
            assert ElementTree.fromstring(settings).find(".//Command").get("Name") == "dotnet-fluxflow"
    shutil.copyfile(tool, feed / f"dotnet-fluxflow.{version}.nupkg")
    sdks = subprocess.check_output(["dotnet", "--list-sdks"], text=True).splitlines()
    outputs = []
    for major in (10, 11):
        versions = [line.split()[0] for line in sdks if line.startswith(f"{major}.")]
        if not versions:
            raise RuntimeError(f".NET {major} SDK is required for the local-tool check.")
        directory = work / str(major)
        directory.mkdir()
        (directory / "global.json").write_text(json.dumps({"sdk": {"version": versions[-1], "rollForward": "disable"}}))
        (directory / "NuGet.Config").write_text(
            '<configuration><packageSources><clear/><add key="local" value="../feed"/></packageSources></configuration>'
        )
        env = tool_environment(directory)
        for args in (["new", "tool-manifest"],
                     ["tool", "install", "--local", "dotnet-fluxflow", "--version", version, "--configfile", "NuGet.Config"],
                     ["fluxflow", "generate", "--help"]):
            subprocess.run(["dotnet", *args], cwd=directory, env=env, check=True, timeout=90)
        cli = ["dotnet", "fluxflow", "generate"]
        expected = check(directory, source, logger, cli, env)

        # Replay the manifest with an empty NuGet cache and tool resolver cache.
        restored = work / f"restored-{major}"
        restored.mkdir()
        manifest = next(p for p in (Path("dotnet-tools.json"), Path(".config/dotnet-tools.json"))
                        if (directory / p).is_file())
        (restored / manifest).parent.mkdir(exist_ok=True)
        for path in (manifest, "global.json", "NuGet.Config", "application.json", "values.json"):
            shutil.copyfile(directory / path, restored / path)
        env = tool_environment(restored)
        subprocess.run(["dotnet", "tool", "restore", "--configfile", "NuGet.Config"],
                       cwd=restored, env=env, check=True, timeout=90)
        generator = Path(__file__).resolve().with_name("generate-app.cs")
        for command in (cli, ["dotnet", "run", "--file", generator, "--"]):
            output = restored / "App.cs"
            subprocess.run([*command, "application.json", output, source, logger],
                           cwd=restored, env=env, check=True, timeout=90)
            assert output.read_bytes() == expected, f"Generated bytes differ for {command} on SDK {versions[-1]}"
            output.unlink()
        outputs.append(expected)
        print(f"Install, clean restore and legacy script passed on SDK {versions[-1]}.")
    assert outputs[0] == outputs[1], "Generated C# differs between SDKs"


if __name__ == "__main__":
    if len(sys.argv) not in (3, 4):
        sys.exit(__doc__)
    with tempfile.TemporaryDirectory() as work:
        source, logger = (Path(arg).resolve() for arg in sys.argv[1:3])
        if len(sys.argv) == 4:
            check_local_tool(Path(work), source, logger, Path(sys.argv[3]).resolve())
        else:
            generator = Path(__file__).resolve().with_name("generate-app.cs")
            check(Path(work), source, logger, ["dotnet", "run", "--file", generator, "--"])
    print("Package selection, mapping, link and exact-byte checks passed.")
