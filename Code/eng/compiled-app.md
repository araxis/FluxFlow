# Generate a file-based workflow application

The FluxFlow CLI translates a Studio export to C# at build time. Its NuGet package and tool command are named `dotnet-fluxflow`, which exposes the .NET CLI command `dotnet fluxflow`. Each family supplies its fluent methods and mapping in an opt-in NuGet package. The .NET SDK restores the selected packages and compiles the emitted file. At execution time FluxFlow composes and runs the graph using the compiled method calls. Handwritten applications enter the same path directly.

In Studio, choose **File → Generate App.cs…** to generate from the current workflow, including unsaved edits. Select the files for relative Source and Mapper expression paths in the save dialog. The desktop host or local browser companion runs the bundled CLI with the family mapping packages. Generation requires a .NET 10 or 11 runtime on that machine. The CLI reports unsupported components by workflow and node name.

Choose **File → Publish compiled application…**, select **.NET 10** or **.NET 11**, and choose **Continue** to build the current workflow into a single Windows x64 executable. Choose the runtime installed on the destination machine. Generate and compiled Publish use the selected Run environment for a single application. An existing service binding supplies its effective endpoint and settings. A deployment that needs Studio to start a container, or defines multiple application instances, is reported before generation. Studio uses the .NET 11 SDK for both targets, supplies the FluxFlow packages, and restores external dependencies from nuget.org. The executable carries the selected Source data and can run with Studio closed.

To run the same generator directly, pack the CLI from the repository with the pinned .NET 11 SDK using `dotnet pack Code/src/Authoring/FluxFlow.Cli -c Release -o <local-feed>`. This produces one package for .NET 10 and 11. From the workflow's build directory, install the tool from that feed. Create the manifest once if the project does not already have one.

~~~powershell
dotnet new tool-manifest
dotnet tool install --local dotnet-fluxflow --version 1.0.0-alpha.3 --add-source ./feed
$packages = (Get-ChildItem ./feed -Filter '*.nupkg').FullName
dotnet fluxflow generate ./inputs/application.json ./App.cs @packages
dotnet publish App.cs -c Release -r win-x64 --no-self-contained --configfile ./NuGet.Config
~~~

For a single-step build from an export, use the [packaged publish command](../src/Authoring/FluxFlow.Cli/README.md). Studio and the CLI share the Windows x64 SDK publish implementation. The published Studio distribution supplies a ready-to-use local tool and package feed. For example, with that feed at `./feed`, run:

~~~powershell
dotnet fluxflow publish ./inputs/application.json --framework net10.0 --feed ./feed --output ./out/Orders.exe
~~~

The tool manifest pins the CLI version. On a fresh checkout, use `dotnet tool restore --add-source ./feed`, or run `dotnet tool restore` when the feed is already configured in NuGet.Config. The generator reads each package's `fluxflow/csharp-mapping.json` and uses its NuGet ID and version from the package manifest. The generated source contains one pinned reference per used mapping package. NuGet resolves its runtime dependencies. Supply one version of each mapping. Conditional and cross-workflow links also use the matching configuration and expression packages from the feed.

The mapping packages cover the 37 components in the shipping plugin catalog. See [component coverage](compiled-component-coverage.md) for each family's resources, verified modes, native prerequisites and remaining checks. The generator supports direct links, remote workflow ports, named router outputs, error-only links and JSONata link conditions. It resolves selected relative files from the export directory.

HTTP Trigger executables also require the ASP.NET Core runtime matching the selected target. The host writes its loopback listener address to the console. Wait for the host startup message before sending requests. For an HTTP connection, supply saved authentication secrets and custom header values on the destination through the environment variables named in App.cs. For example, set `Resources__api__Authentication__Token` for the bearer token of the application resource `api`. For an OTLP collector named `logs`, set `Resources__logs__Headers` to the collector header list when the resource specifies headers. The exporter package is selected only for collector resources. MQTT certificate files belong on the destination. For a connection named `broker`, set `Resources__broker__ClientCertificate__Path` and, when configured, `Resources__broker__ClientCertificate__PrivateKeyPath` and `Resources__broker__ClientCertificate__Password`. Set one path per configured trust anchor, starting with `Resources__broker__TrustedCertificates__0`. The client and trust material is loaded by the normal MQTT registration.

Source embeds exact file bytes. Mapper requires `Engine: jsonata`, accepts `HandleErrors` (default false) and embeds the resolved expression from inline text, Base64 or a selected file. Mapper uses the host's registered JSONata engine when provided. Scheduler, Source, Mapper and Logger default to an output capacity of 128. Unsupported settings, missing files, duplicate mappings and unknown target ports fail before the output is written.

For handwritten applications use the same `WorkflowBuilder` on your host's service collection. The Scheduler, Source, Mapping and Logging fluent packages register their nodes and ports. The host supplies the logging provider and owns Start and Stop. The generated console program stops on Enter and also observes application completion. The published executable requires the matching .NET runtime.

To check a packed CLI against both SDKs, run `python Code/eng/test-generate-app.py <Source.Fluent.nupkg> <Logging.Fluent.nupkg> <dotnet-fluxflow.nupkg>` from the repository root. For each SDK, the check installs from a local feed, restores the copied manifest with empty caches, and compares the generated bytes with the legacy `eng/generate-app.cs` entry point.

## Test a compiled Studio export on Windows x64

Use the .NET 11 Studio distribution produced by `Code/eng/publish-studio.ps1`. The two-target check also needs the Windows x64 .NET 10 runtime installed alongside the SDK.

~~~powershell
python Code/eng/test-studio-compiled-app.py <studio-distribution>
~~~

The test edits the Mapper expression and Logger category and sets all four output capacities to 1 in Studio's in-memory graph. It checks the generated calls, exact Source bytes and Undo/Redo. Use `--case orders` to run the order trial on its own. It runs the comparison twice, once with an inline expression and once with a relative expression file. For each form it starts the workflow twice through the Studio companion and publishes the same export for both runtimes. After closing the companion and removing the inputs, it starts each EXE twice and compares the order, severity, category and rendered totals against Studio's retained logs. B-23 totals 2,950 cents and C-42 totals 1,199 cents. The isolated .NET 10 runtime runs the .NET 10 EXE and rejects the .NET 11 EXE. An unknown Mapper engine or a missing package must produce a diagnostic and no download.

To compare the independent Generate App.cs and SDK path, pass a matching flat runtime feed as a second argument. That trial also accepts `--dependencies` for the external NuGet source.
