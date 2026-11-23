# FluxFlow SDK

Install the SDK once per user on the Windows x64 build machine with the .NET 11 SDK. From the directory containing the release package:

```powershell
dotnet tool install --global FluxFlow.Sdk --version 1.0.0-alpha.1 --source .
```

In the workflow's directory, use the installed command with a Studio configuration export:

```powershell
fluxflow publish ./application.json --framework net11.0 --output ./Orders.exe
fluxflow generate ./application.json ./App.cs
```

The tool includes its family mappings and FluxFlow runtime packages. Relative Source and expression paths resolve beside the export. An initial publish needs access to nuget.org for external dependencies and any required SDK packs. Use `--framework net10.0` for .NET 10 or add `--runtime linux-x64` and choose an output such as `./Orders` for Linux. The destination needs the matching runtime.

Use `dotnet tool update --global FluxFlow.Sdk --version <version> --source <feed>` to install a later release. Remove the command with `dotnet tool uninstall --global FluxFlow.Sdk`. Teams can also install the SDK in a project tool manifest using `--local`. The existing `dotnet-fluxflow` CLI and ZIP kit remain available.
