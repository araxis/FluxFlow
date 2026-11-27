# FluxFlow SDK

Install the SDK once per user on the Windows x64 build machine with the .NET 11 SDK. From the directory containing the release package:

```powershell
dotnet tool install --global FluxFlow.Sdk --version 1.0.0-alpha.6 --source .
```

In the workflow's directory, use the installed command with a Studio configuration export:

```powershell
fluxflow publish ./application.json --framework net11.0 --output ./Orders.exe
fluxflow generate ./application.json ./App.cs
```

The tool includes its family mappings and FluxFlow runtime packages. Relative Source and expression paths resolve beside the export. An initial publish needs access to nuget.org for external dependencies and any required SDK packs. Use `--framework net10.0` for .NET 10 or add `--runtime linux-x64` and choose an output such as `./Orders` for Linux. The destination needs the matching runtime.

Use `dotnet tool update --global FluxFlow.Sdk --version <version> --source <feed>` to install a later release. Remove the command with `dotnet tool uninstall --global FluxFlow.Sdk`. Teams can also install the SDK in a project tool manifest using `--local`. The existing `dotnet-fluxflow` CLI and ZIP kit remain available.

## Windows services

Run the SDK in an elevated terminal on the destination Windows machine. From the directory containing a Studio configuration export, select a workflow, publish it, and register it as a Windows service with one command.

```powershell
fluxflow service install Orders
fluxflow service start Orders
fluxflow service stop Orders
fluxflow service uninstall Orders
```

Install scans the current directory's JSON files and uses the only valid FluxFlow configuration. If several qualify, it lists the candidates and exits. Use `--interactive` for a numbered selection, or pass an exact path such as `fluxflow service install Orders ".\exports\orders.json"`. Relative data and expression files resolve beside that export. An explicit invalid file reports its own error.

The default build targets Windows x64 on .NET 11. Use `--framework net10.0` for .NET 10. The executable is installed under `%ProgramFiles%\FluxFlow\Services\service-Orders\App.exe` and runs as LocalService. Installation defaults to manual startup. Use `--startup automatic` for automatic startup at boot. A duplicate service or an existing deployment directory is preserved. An explicit published EXE can still be registered in place.

Start waits for the Running state. Stop waits for the Stopped state and for the process to exit when it was running. A clean stop returns 0. If work outlives the drain deadline, Stop reports Incomplete and returns 2. A cleanup or control failure returns 1.

Uninstall uses the Windows service name. It stops a running service and checks its process exit before removing the registration. If that Stop fails or reports Incomplete, the registration stays in place. The deployment folder, executable and configuration are preserved. To register the retained build again, pass its existing EXE to `service install`.

## Linux system services

On an x64 Linux machine running systemd, run the same commands as your ordinary user. Install needs the build SDK and the service needs a system-wide runtime matching the chosen .NET target. The workflow and relative inputs are compiled before the tool asks for administrator approval. A terminal uses sudo. A graphical Studio host can use a polkit authentication agent. An unattended host needs administrator authorization configured separately.

```sh
fluxflow service install Orders application.json --framework net10.0 --startup automatic
fluxflow service start Orders
fluxflow service stop Orders
fluxflow service uninstall Orders
```

Install registers `fluxflow-Orders.service` in the system manager. Manual startup is the default. Automatic startup enables the unit for subsequent boots, including before login. Start launches it immediately. The service runs under a dedicated dynamic user with a writable state directory. Use `journalctl -u fluxflow-Orders.service` to read its logs. Uninstall stops the service, disables startup and unregisters the unit. It retains the deployment under `/var/lib/fluxflow/services/service-Orders` and the application's state. Installation refuses to overwrite an existing deployment directory.
