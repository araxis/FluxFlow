# FluxFlow CLI

The pinned tool also supports `dotnet fluxflow service install`, `start` and `stop` on Windows. When building a workflow during installation, pass `--feed <folder>` with the pinned CLI. The global SDK supplies its own feed. See the [service commands](../FluxFlow.Sdk/README.md#windows-services) for input discovery, permissions and shutdown outcomes.

Publish a Studio configuration export from a Windows x64 build machine with the .NET 11 SDK. The published Studio distribution includes the tool, family mappings and runtime packages in `app-generator/feed`. Set `$feed` to that folder. From the directory that will own the tool manifest, run the following once.

~~~powershell
dotnet new tool-manifest
dotnet tool install --local dotnet-fluxflow --version 1.0.0-alpha.9 --configfile "$feed/NuGet.Config"
~~~

Commit the manifest. On a clean checkout, restore the pinned tool and choose the runtime installed on the deployment PC.

~~~powershell
dotnet tool restore --configfile "$feed/NuGet.Config"
dotnet fluxflow publish ./inputs/application.json --framework net10.0 --feed "$feed" --output ./out/net10/Orders.exe
dotnet fluxflow publish ./inputs/application.json --framework net11.0 --feed "$feed" --output ./out/net11/Orders.exe
dotnet fluxflow publish ./inputs/application.json --framework net11.0 --runtime linux-x64 --feed "$feed" --output ./out/linux/Orders
~~~

Publish reads mapping packages from the specified flat local feed and pins the ones used by the export. Relative Source and expression paths resolve beside the export. The build uses the feed's `NuGet.Config` when present. Otherwise it restores FluxFlow packages from the feed and external dependencies from nuget.org. Each publish uses an isolated SDK workspace and package cache. A successful build replaces the destination executable. A failed or canceled build preserves the previous file.

The default runtime identifier is `win-x64`. Both framework versions also support `--runtime linux-x64`. Copy the executable to an x64 machine with the matching .NET runtime. On Linux, run `chmod +x ./Orders` after copying from Windows and start it with `./Orders`. A workflow using a Windows-only mapping, such as BLE GATT Server, requires a Windows target. Workflows using HTTP Trigger also require the matching ASP.NET Core runtime. Supply runtime credentials through the references already configured for the workflow.

To keep the generated C# for inspection, use the generator and supply the family packages. To check a deployment target during generation, add `--runtime linux-x64` or `--runtime win-x64` before the packages and after any `--framework` option.

~~~powershell
$packages = (Get-ChildItem $feed -Filter '*.nupkg').FullName
dotnet fluxflow generate ./inputs/application.json ./App.cs @packages
~~~
