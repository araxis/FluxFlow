# FluxFlow plugins, contract v1

A plugin is a trusted folder with a manifest and a public, parameterless entry class implementing
`IFluxFlowPlugin`. Use the same stable component, connection and port identifiers across
releases: existing project files refer to those identifiers and do not contain package paths.
Installing or removing a plugin changes the host, never the project.

## Install from a folder, ZIP or local NuGet feed

From the repository root, with the pinned .NET 11 SDK and .NET 8, 10 and 11 runtimes for tests:

~~~powershell
dotnet pack Code/src/Plugins/FluxFlow.Plugins -c Release -o Code/artifacts/plugin-feed
dotnet pack Code/src/Plugins/FluxFlow.Plugins.Serial -c Release -o Code/artifacts/plugin-feed
./Code/eng/fluxflow-plugin.ps1 Install -Id FluxFlow.Plugins.Serial -Version 1.0.0-alpha.5 -Feed Code/artifacts/plugin-feed
./Code/eng/fluxflow-plugin.ps1 List
~~~

Alternatively, install the same payload directly (choose one source):

~~~powershell
./Code/eng/fluxflow-plugin.ps1 Install -Source ./Serial
./Code/eng/fluxflow-plugin.ps1 Install -Source ./Serial.zip
./Code/eng/fluxflow-plugin.ps1 Install -Source ./Code/artifacts/plugin-feed/FluxFlow.Plugins.Serial.1.0.0-alpha.5.nupkg
~~~

Put `fluxflow-plugin.json` at the folder root, beside `lib/net8.0/`, `lib/net10.0/` and any
supporting files. ZIPs may also contain one enclosing folder. Installation copies the complete
payload; moving or changing the source afterward does not affect the installed plugin.
Direct folder/ZIP installation needs PowerShell 7, not the NuGet CLI or .NET SDK.

Contract v1 keeps those two payload targets. .NET 8 companions select `net8.0`;
.NET 10 and .NET 11 companions select `net10.0`, for runtime and native design
dependencies together. The .NET 11 browser consumes the `net10.0` portable design
payload. Existing packages do not need rebuilding for the host upgrade. A package
containing only `net11.0` is not a contract v1 package.

Start the companion and reload Studio. Serial Text, Reader and Writer appear in the palette,
along with the workflow-scoped Serial port connection, Inspector fields, documentation and
host device discovery. To remove Serial, close the host and run:

~~~powershell
./Code/eng/fluxflow-plugin.ps1 Remove -Id FluxFlow.Plugins.Serial
~~~

Restart the host and reload Studio. Existing projects retain their configuration and links;
Check environment identifies unavailable components. Repeat Install and restart to restore
execution. Remove deletes the installed copy. Original receipt-based installations still load; removing one leaves its old NuGet cache intact. For a private test
store set `FLUXFLOW_PLUGIN_DIRECTORY` for both the script and the host. By default they use
the current user's local application-data directory under `FluxFlow/Plugins`. The host holds
a shared lock so packages cannot change underneath an active run.

## Author a plugin

Reference the `FluxFlow.Plugins` SDK and the FluxFlow runtime APIs your components use.
The SDK itself depends on DI abstractions, not on Studio, hardware libraries or a plugin framework.
Supply one entry DLL named after the plugin ID in each of `lib/net8.0` and
`lib/net10.0`, and a root `fluxflow-plugin.json`, for example:

~~~json
{"contract":1,"id":"FluxFlow.Plugins.Serial","version":"1.0.0-alpha.5","entryType":"FluxFlow.Plugins.Serial.SerialPlugin"}
~~~

- `Description` supplies stable type IDs, palette and canvas ports, inspector sections and
  defaults, connection scope and documentation. Supply metadata without opening ports or
  contacting services. The browser consumes this description; it does not download or
  execute the plugin's native implementation. Use the shared property controls. Serial declares its frame fields, validation rules and port suggestions;
  the host renders them and sends edits through its normal edit history.
- `ConfigureWorkflow` registers component and resource factories using the existing
  configuration APIs. Both Check environment and Start use it. Registration must be
  repeatable and must not acquire devices. Acquire resources during execution and let the
  workflow dispose them on Stop.
- `ConfigureHost` registers companion services such as environment adapters or device discovery.
- Expression properties may declare `PreviewProvider` and `PreviewFields`. Register a
  keyed `PluginExpressionPreview` from `ConfigureHost` to compile an isolated sample
  with those settings. The shared editor keeps syntax highlighting, source handling,
  sample metadata, errors and Save/Cancel. The host rejects file preview before invoking
  the provider. The provider receives a complete JSON message envelope and returns the
  result value; it must not start a workflow or mutate saved settings. Sources uses its
  existing generator evaluator with Locale. These optional members are in SDK
  `1.0.0-alpha.7`; older properties retain the ordinary expression-engine path.
  In `1.0.0-alpha.8`, `PreviewSample` optionally supplies initial JSON for a custom
  value-only context. The shared workspace then omits message metadata and error-input
  controls. Samples remain local drafts and never enter component configuration.
  Providers may also return Boolean results when the property declares that result kind.
- `ConfigureWorkflowHost` optionally configures the native `IHostBuilder` for one run instance.
  The default implementation does nothing, preserving existing plugin implementations.
  Configure host services here without starting them; the runner owns startup and cleanup.
  The HTTP plugin uses this hook for its loopback listener and existing trigger route mapping.
- `Functions` optionally supplies named JSONata delegates and their signatures. Give functions
  distinct, plugin-specific names (without the leading dollar sign). The companion's preview and
  runtime bind these delegates into each evaluation; removing the plugin removes its functions.
- For NuGet sources the package ID and version must match any identity declared in the manifest. The plugin ID must match the manifest and description. Bundle private managed helpers beside
  the entry DLL; shared contract and runtime assemblies come from the host. The local feed option selects the exact package from a flat or standard hierarchical feed. The installer does not restore dependencies. Version is optional for a loose payload. Installing a plugin grants it the same access as the companion.

For portable property editors, declare sections, field kinds, choices and defaults in the catalog.
Set a component's editor to `properties` for an inline grid and Advanced JSON. A connection text
field can declare a `SuggestionSource`; register its read-only, cancellable provider under
`SuggestionKey(pluginId, operation)` in the companion. The operation offers choices without
restricting manual entries. A `PluginDesignEntry` names a managed `IPluginDesign` class and
its dependencies under `lib/<tfm>/`. Design hosts load those assemblies in order and call
`Component` or `Connection` for field visibility and validation. They do not load device drivers
or UI controls. Serial uses field suggestions. MQTT uses a portable row collection for topic
filters and QoS, an activation condition on the ACK input, and connection property groups for
TLS and reconnect settings. A wired inactive port retains its address and links.

For hierarchy editing, supply `PropertyRules.Hierarchy`. Its detached document describes the tree, fields,
choices and actions; the host handles Visual/JSON, Save/Cancel and history. Read a complete saved document,
preserve fields you do not own, and validate on Save. For live discovery, use `PropertyRules.Interactive`
with an `ISelectionExplorer` session. Keep discovery, conversion, cancellation and stale-result rules in
the portable design. The Bluetooth plugin demonstrates both contracts and a driver-free value codec.
These capabilities are in the 1.0.0-alpha.5 SDK. Existing StudioEntry packages remain supported;
the shipped component families use portable definitions.

Managed dependencies can go beside the runtime entry DLL,
OS-specific managed dependencies under `win/` or `unix/`, and native dependencies under
the runtime identifier, e.g. `linux-x64/`. 

## Manage plugins in Studio

Open **Plugins > Settings** to save a named folder on the companion computer or a named NuGet v3 feed. Existing feeds are preserved, and **nuget.org** remains the default. Saving sources takes effect without a restart and does not change the project.

In **Plugins > Manage plugins > Install plugin**, choose a saved source by name. For a folder, select a plugin from the list: Studio recognizes a folder containing `fluxflow-plugin.json`, immediate child plugin folders, and local ZIP or NuGet packages. For a feed, enter the exact package ID and version. You can also browse for a folder or archive directly (uploads up to 64 MiB), or choose **Custom feed URL…** for a one-time install. The companion checks downloaded package identity before staging. Restart the companion to activate pending installs. The **Source** column shows the installation method; older installations may show **Not recorded**. Management requires PowerShell 7 on that computer.

Studio stages the validated package for the next start. A pending removal also leaves the running plugin available. Either change can be cancelled in the list before restart. Close all companions sharing the store (including desktop Studio), restart the one you use, and reload browser Studio to see the updated palette. Plugin management does not edit saved workflows or their undo history.

A component with nested editable lists can provide `PropertyRules.ObjectGroups` with a root property, list definitions, field descriptors and creation defaults. An optional named map supplies detached typed-value drafts, including lists and nested maps. The host renders fields and commits accepted changes through its normal edit history. For a working example, see the [RabbitMQ plugin](../FluxFlow.Plugins.RabbitMQ/README.md).
