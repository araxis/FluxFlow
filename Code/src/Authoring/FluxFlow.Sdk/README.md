# FluxFlow SDK

Install the SDK once per user on the Windows x64 build machine with the .NET 11 SDK. From the directory containing the release package:

```powershell
dotnet tool install --global FluxFlow.Sdk --version 1.0.0-alpha.2 --source .
```

In the workflow's directory, use the installed command with a Studio configuration export:

```powershell
fluxflow publish ./application.json --framework net11.0 --output ./Orders.exe
fluxflow generate ./application.json ./App.cs
```

The tool includes its family mappings and FluxFlow runtime packages. Relative Source and expression paths resolve beside the export. An initial publish needs access to nuget.org for external dependencies and any required SDK packs. Use `--framework net10.0` for .NET 10 or add `--runtime linux-x64` and choose an output such as `./Orders` for Linux. The destination needs the matching runtime.

Use `dotnet tool update --global FluxFlow.Sdk --version <version> --source <feed>` to install a later release. Remove the command with `dotnet tool uninstall --global FluxFlow.Sdk`. Teams can also install the SDK in a project tool manifest using `--local`. The existing `dotnet-fluxflow` CLI and ZIP kit remain available.

## Windows services

Publish the application with this SDK and put its executable in its intended installation directory. The LocalService account needs read and execute access. In an elevated terminal on the destination Windows machine, register the executable, then start or stop it by service name.

```powershell
fluxflow service install Orders "C:\Services\Orders\Orders.exe"
fluxflow service start Orders
fluxflow service stop Orders
```

Installation defaults to manual startup. Add `--startup automatic` to the install command for automatic startup at boot. Installation registers the existing executable in place. A duplicate registration fails. Start waits for the Running state. Stop waits for the Stopped state and for the process to exit when it was running. A clean stop returns 0. If work outlives the drain deadline, Stop reports Incomplete and returns 2. A cleanup or control failure returns 1.
