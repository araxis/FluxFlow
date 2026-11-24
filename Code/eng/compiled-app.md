# Generate a file-based workflow application

For everyday command-line publishing, install the FluxFlow SDK once per user. On a Windows x64 machine with the .NET 11 SDK, run this from the folder containing the release's `FluxFlow.Sdk.1.0.0-alpha.2.nupkg`.

~~~powershell
dotnet tool install --global FluxFlow.Sdk --version 1.0.0-alpha.2 --source .
~~~

Then run this from any folder containing a Studio configuration export. The installed SDK supplies the mapping and runtime packages.

~~~powershell
fluxflow publish ./application.json --framework net11.0 --output ./Orders.exe
~~~

See the [SDK commands](../src/Authoring/FluxFlow.Sdk/README.md) for generation, Linux targets and updating or removing the tool.

The FluxFlow CLI translates a Studio export to C# at build time. Its NuGet package and tool command are named `dotnet-fluxflow`, which exposes the .NET CLI command `dotnet fluxflow`. Each family supplies its fluent methods and mapping in an opt-in NuGet package. The .NET SDK restores the selected packages and compiles the emitted file. At execution time FluxFlow composes and runs the graph using the compiled method calls. Handwritten applications enter the same path directly.

In Studio, choose **File → Generate App.cs…** to generate from the current workflow, including unsaved edits. Select the files for relative Source and Mapper expression paths in the save dialog. The desktop host or local browser companion runs the bundled CLI with the family mapping packages. Generation requires a .NET 10 or 11 runtime on that machine. The CLI reports unsupported components by workflow and node name.

Choose **File → Publish compiled application…**, select **Windows x64** or **Linux x64** and **.NET 10** or **.NET 11**, then choose **Continue**. Match the destination OS and installed runtime. For Orders.fluxflow, the save dialog suggests Orders.exe on Windows and Orders on Linux. An unnamed project uses App.exe or App. After copying a Linux executable from Windows, grant execute permission with chmod +x ./Orders. You can edit the filename before publishing. Generate and compiled Publish use the selected Run environment for a single application. An existing service binding supplies its effective endpoint and settings. A deployment that needs Studio to start a container, or defines multiple application instances, is reported before generation. Studio uses the .NET 11 SDK for both targets, supplies the FluxFlow packages, and restores external dependencies from nuget.org. The executable carries the selected Source data and can run with Studio closed.

To run the same generator directly, pack the CLI from the repository with the pinned .NET 11 SDK using `dotnet pack Code/src/Authoring/FluxFlow.Cli -c Release -o <local-feed>`. This produces one package for .NET 10 and 11. From the workflow's build directory, install the tool from that feed. Create the manifest once if the project does not already have one.

~~~powershell
dotnet new tool-manifest
dotnet tool install --local dotnet-fluxflow --version 1.0.0-alpha.7 --add-source ./feed
$packages = (Get-ChildItem ./feed -Filter '*.nupkg').FullName
dotnet fluxflow generate ./inputs/application.json ./App.cs @packages
dotnet publish App.cs -c Release -r win-x64 --no-self-contained --configfile ./NuGet.Config
~~~

For a single-step build from an export, use the [packaged publish command](../src/Authoring/FluxFlow.Cli/README.md). Studio and the CLI share the SDK publish implementation. The CLI accepts `--runtime win-x64` (default) or `--runtime linux-x64`. The published Studio distribution supplies a local tool and package feed.

~~~powershell
dotnet fluxflow publish ./inputs/application.json --framework net10.0 --feed ./feed --output ./out/Orders.exe
dotnet fluxflow publish ./inputs/application.json --framework net10.0 --runtime linux-x64 --feed ./feed --output ./out/Orders
~~~

The tool manifest pins the CLI version. On a fresh checkout, use `dotnet tool restore --add-source ./feed`, or run `dotnet tool restore` when the feed is already configured in NuGet.Config. The generator reads each package's `fluxflow/csharp-mapping.json` and uses its NuGet ID and version from the package manifest. The generated source contains one pinned reference per used mapping package. NuGet resolves its runtime dependencies. Supply one version of each mapping. Conditional and cross-workflow links also use the matching configuration and expression packages from the feed.

The mapping packages cover the 37 components in the shipping plugin catalog. See [component coverage](compiled-component-coverage.md) for each family's resources, verified modes, native prerequisites and remaining checks. The generator supports direct links, remote workflow ports, named router outputs, error-only links and JSONata link conditions. It resolves selected relative files from the export directory.

HTTP Trigger executables also require the ASP.NET Core runtime matching the selected target. The host writes its loopback listener address to the console. Wait for the host startup message before sending requests. For an HTTP connection, supply saved authentication secrets and custom header values on the destination through the environment variables named in App.cs. For example, set `Resources__api__Authentication__Token` for the bearer token of the application resource `api`. For an OTLP collector named `logs`, set `Resources__logs__Headers` to the collector header list when the resource specifies headers. The exporter package is selected only for collector resources. MQTT certificate files belong on the destination. For a connection named `broker`, set `Resources__broker__ClientCertificate__Path` and, when configured, `Resources__broker__ClientCertificate__PrivateKeyPath` and `Resources__broker__ClientCertificate__Password`. Set one path per configured trust anchor, starting with `Resources__broker__TrustedCertificates__0`. The client and trust material is loaded by the normal MQTT registration.

Source embeds exact file bytes. Mapper requires `Engine: jsonata`, accepts `HandleErrors` (default false) and embeds the resolved expression from inline text, Base64 or a selected file. Mapper uses the host's registered JSONata engine when provided. Scheduler, Source, Mapper and Logger default to an output capacity of 128. Unsupported settings, missing files, duplicate mappings and unknown target ports fail before the output is written.

For handwritten applications use the same `WorkflowBuilder` on your host's service collection. The Scheduler, Source, Mapping and Logging fluent packages register their nodes and ports. The host supplies the logging provider and owns Start and Stop. The generated console program stops on Enter and also observes application completion. The published executable requires the matching .NET runtime.

To check a packed CLI against both SDKs, run `python Code/eng/test-generate-app.py <Source.Fluent.nupkg> <Logging.Fluent.nupkg> <dotnet-fluxflow.nupkg>` from the repository root. For each SDK, the check installs from a local feed, restores the copied manifest with empty caches, and compares the generated bytes with the legacy `eng/generate-app.cs` entry point.

## Service deployment milestone

The Orders publishing trial passed on Windows x64 and Ubuntu x64 under WSL for .NET 10 and 11. Service deployment has a separate acceptance checklist. Each user-facing deployment action must be available through both the CLI and Studio, using the same implementation.

On the first Stop request, stop accepting new independent work and give accepted work a finite grace period to finish. Keep replies and acknowledgements available for accepted work during that period. Repeated Stop requests join the same shutdown and share its deadline. If the work deadline expires, cancel outstanding work and record its outcome as **Incomplete**. Leave unfinished deliveries unacknowledged. Redelivery depends on the source's delivery guarantees. The grace-period duration remains open for review.

Every orderly Stop path, including a work timeout or processing error, must run cleanup. Detach handlers, stop consumers, cancel timers, and close run-owned channels and connections. A cleanup error on one resource must allow the others to be released. CLI and Studio may confirm Stop only when cleanup is verified, even if work ended incomplete. Report cleanup failures or unconfirmed cleanup separately and preserve the original errors. A forced process kill or loss of power can interrupt cleanup. Durable broker subscriptions follow their configured retention policy.

- [ ] **Service lifetime.** Start the compiled Orders workflow with no console input. It must process both orders and stay available until asked to stop. A service-manager Stop must drain the workflow and report its real outcome. Restart it and verify both orders again. Preserve Enter-to-stop for interactive use.
- [ ] **Windows Service and Linux systemd.** Install, inspect status, start, stop, restart and uninstall an isolated test service on each platform. Verify manual startup and automatic startup at boot, including an actual boot in an isolated environment. Uninstall must preserve the published application and its configuration.
- [ ] **Studio and CLI parity.** Exercise each action and startup choice through both entry points. Match the resulting service state and error details. Check cancellation before applying changes, insufficient permissions and previously installed services. Keep deployment actions outside workflow Undo/Redo.

The generated application now uses the .NET host lifetime. Enter still requests Stop, and closed standard input allows headless execution to continue. Temporary Windows services and Ubuntu systemd user services each started and stopped Orders twice per target on .NET 10 and 11. The native Windows check verified the service state and process exit.

The separate Windows RabbitMQ trial uses `restart_trial.py --windows-control-stop 10 11` with the compiled trial executables. It stops the headless process after B commits but before broker acknowledgement. Both targets released the consumer and connection, redelivered B with a new token, reused the SQLite receipt and finished C.

Run `python Code/eng/test-compiled-shutdown.py` with the installed SDK to reproduce deadline expiry. The test holds an HTTP request beyond the host's default 30-second work grace period, checks HTTP 503, verifies that the listener closes and the process returns 2, then restarts the executable and verifies a clean stop. Native Windows Service Manager tests also verified the timeout status (1460) and a clean subsequent Stop on both .NET targets. See the [Windows service commands](../src/Authoring/FluxFlow.Sdk/README.md#windows-services).

## Test a compiled Studio export on Windows x64

Use the .NET 11 Studio distribution produced by `Code/eng/publish-studio.ps1`. The two-target check also needs the Windows x64 .NET 10 runtime installed alongside the SDK.

~~~powershell
python Code/eng/test-studio-compiled-app.py <studio-distribution>
~~~

The test edits the Mapper expression and Logger category and sets all four output capacities to 1 in Studio's in-memory graph. It checks the generated calls, exact Source bytes and Undo/Redo. Use `--case orders` to run the order trial on its own. It runs the comparison twice, once with an inline expression and once with a relative expression file. For each form it starts the workflow twice through the Studio companion and publishes the same export for both runtimes. After closing the companion and removing the inputs, it starts each EXE twice and compares the order, severity, category and rendered totals against Studio's retained logs. B-23 totals 2,950 cents and C-42 totals 1,199 cents. The isolated .NET 10 runtime runs the .NET 10 EXE and rejects the .NET 11 EXE. An unknown Mapper engine or a missing package must produce a diagnostic and no download.

To compare the independent Generate App.cs and SDK path, pass a matching flat runtime feed as a second argument. That trial also accepts `--dependencies` for the external NuGet source.
