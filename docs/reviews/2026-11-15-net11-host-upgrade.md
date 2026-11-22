# .NET 11 host upgrade

## Scope

Studio WebAssembly now targets .NET 11. The web companion, standalone runner,
shared Studio UI, execution host and Windows desktop add .NET 11 targets while
retaining their existing .NET 8 and .NET 10 targets. Windows target suffixes are
preserved. Run/publish scripts default to .NET 11. The repository pins SDK
11.0.100, permits servicing patches and excludes preview SDKs.

Framework package updates apply only to .NET 11 targets. Plugin contract v1,
plugin runtime/design payload targets, project JSON, configuration models and edit
history are unchanged. Core sources and project files are unchanged; WFENG was
not modified. Optional C# 15 rewrites, trimming and AOT remain outside this change.

## Compatibility fixes

1. Native plugin design assembly resolution previously used the host runtime's
   major version as a literal folder name. A .NET 11 host therefore searched
   `net11.0` even though the existing plugin runtime loaded from `net10.0`.
   Runtime loading and native editor resolution now share the compatible target
   selection. Browser design dependencies continue to use `net10.0`. The existing
   install/remove/reinstall regression now checks both native and browser paths;
   it failed before the fix and passed afterward.
2. The browser publisher reused a single-target artifacts directory across
   framework upgrades. A real publish after the .NET 10 baseline retained old
   managed WebCIL assets alongside the .NET 11 native runtime. The browser stopped
   at startup with `mono_wasm_bind_cs_function` undefined. Publish intermediates
   are now separated by the browser's evaluated target framework. The corrected
   distribution was launched and exercised successfully.

## Validation on Windows

The pre-change Studio suite passed 1,941 tests on the .NET 10 runtime. Old plugin
packages and a .NET 10 Studio publication were retained before the upgrade. The
machine had SDK 11.0.100 installed; this was a runtime/source baseline, not an
SDK 10 compiler comparison.

| Check | Result |
| --- | --- |
| Release solution build | Passed, zero errors |
| Studio tests, .NET 8 / 10 / 11 | 1,941 passed per target |
| Desktop tests, .NET 8 / 10 / 11 | 11 passed per target |
| Environment integration, .NET 11 | 204 passed, including local containers |
| Environment integration excluding Docker, .NET 8 / 10 | 173 passed per target |
| Windows BLE-targeted web companion and runner, .NET 11 | Both built |
| Published WebAssembly, web companion, runner and Windows desktop | Published and launched |

The first integration run had 31 Docker-dependent failures while the local engine
was stopped. After starting the existing engine, all 204 tests passed. This includes
RabbitMQ shutdown/unexpected-disconnect log preservation, MQTT pending-limit Stop,
HTTP draining, controlled BLE operations, plugin removal and saved-project round trips.
No serial hardware or BLE peripheral was actuated in this upgrade.

Pre-upgrade Serial, MQTT, RabbitMQ, NATS, Routing and the four other bundled plugin
packages were installed into an isolated store. All nine loaded in the published
.NET 11 web host. Served portable design dependencies matched the installed
`net10.0` files byte-for-byte. The standalone runner and web companion each ran a
Scheduler → Gate → Mapper → Logger workflow twice, with output, original log text,
Stop retention and rerun isolation checked through their public APIs.

The published desktop rendered its workspace and old plugin palette. Browser
checks exercised Serial's portable editor, numeric entry, Bytes selection and
Undo/Redo. A temporary project was saved and reopened, Serial was removed, and
the project was saved while its editor was unavailable. Every saved JSON section
matched the original. Reinstalling the unchanged old package and reopening that
copy restored Serial with `MaxFrameBytes = 4096` and `OutputFormat = Bytes`.

## Reproduction and rollback

From the repository root:

```powershell
dotnet build Code/FluxFlow.sln -c Release
dotnet test Code/tests/Unit/FluxFlow.Studio.Tests -c Release -p:TestTfmsInParallel=false
dotnet test Code/tests/Unit/FluxFlow.Studio.Desktop.Tests -c Release -p:TestTfmsInParallel=false
dotnet test Code/tests/Integration/FluxFlow.Environments.Tests -c Release -f net11.0
pwsh -File Code/eng/publish-studio.ps1 -Framework net11.0
dotnet publish Code/src/Hosting/FluxFlow.Runner -c Release -f net11.0 -o Code/artifacts/runner/net11.0
dotnet publish Code/src/Studio/FluxFlow.Studio.Desktop -c Release -f net11.0-windows10.0.17763.0 -o Code/artifacts/desktop/net11.0
```

Use a separate `FLUXFLOW_PLUGIN_DIRECTORY` for acceptance trials. Keep the previous
whole distribution and plugin store available for rollback. Reverting only the
companion does not revert the WebAssembly application; use the previous complete
publication. No project-schema conversion or plugin-package rebuild is required.

CI now installs the pinned SDK, publishes the .NET 11 distribution/runner and runs
desktop tests/publication on Windows. Remote CI has not been run from this review.
Local publication was framework-dependent on Windows x64; Linux/macOS launch,
self-contained deployment, performance measurements and hardware behavior are not
claimed. Existing analyzer warnings and newly enabled Blazor interop warnings
remain; no blanket suppressions were added.

## Follow-up review

At the initial follow-up, the canvas dependency remained an open compatibility issue. The pinned
`Z.Blazor.Diagrams` 3.0.4.1 package declares its .NET 10 Blazor dependencies as
`>= 10.0.0 && < 11.0.0`. Restoring the .NET 11 UI therefore produces NU1608.
The published UI checks above passed, but they do not establish support for every
canvas interaction on Blazor 11. The warning is retained. No newer compatible
release was listed in the [package feed](https://www.nuget.org/packages/Z.Blazor.Diagrams)
at review time. Resolve the dependency's supported range before treating the
entire UI upgrade as fully supported; suppressing the warning would not resolve it.

The follow-up review found no additional reproducible defect in plugin target
selection or Stop/rerun cleanup. The .NET 11 environment suite passed all 204
tests again, including the real broker and log-retention cases. Desktop tests
passed all 11 cases on each of .NET 8, 10 and 11, including shutdown with an active
run and preservation of original cleanup failures.

The Studio suite passed 1,941 tests on each target (.NET 8, 10 and 11), covering
configuration round trips and edit history. The seven installed-plugin tests also
passed on each target, including removal/reinstall and native/browser design
payload resolution. An initial `--no-restore` Studio invocation encountered an
assets file left with only the Windows-targeted host variants; rerunning with
restore resolved that build-state issue. No source change was needed.

The previously published .NET 11 web companion and standalone runner were started
on separate temporary ports with a copied plugin store containing the pre-upgrade
packages. Both passed two Scheduler/Gate/Mapper/Logger runs, checking outputs,
original log text, Stop retention and isolation from the previous run. These checks
exercise workflow Stop through the API; terminating the temporary processes
afterward is not evidence of graceful process shutdown. User settings and existing
Studio tabs were not changed.

Only this review report changed during the follow-up. No additional runtime
abstraction or C# 15 refactor was introduced. Core source/project hashes still
match the pre-upgrade snapshot; WFENG was not edited.

## Canvas compatibility resolution

The canvas-specific NU1608 warning is now resolved without suppressing it. The
.NET 11 UI builds the exact upstream UI source from the revision recorded in
`Z.Blazor.Diagrams` 3.0.4.1 against Blazor 11. The diagram engine remains the
original NuGet package. .NET 8 and .NET 10 retain their original UI package.
There are no canvas behavior changes or linked source files. All 50 imported
source, asset and license files match their recorded upstream hashes.

The compatibility project retains the assembly identity and static asset paths,
but uses a distinct internal project ID. Using the original package ID caused
NuGet to omit compile assets for the .NET 8/10 package during multitarget restore;
the distinct ID fixes that collision. See the
[compatibility build notes](../../Code/src/Studio/ThirdParty/Blazor.Diagrams/README.md)
for source provenance and removal instructions when upstream publishes support.
This is a locally verified compatibility build, not an upstream support claim.

Verification:

- Full Release solution build passed with NU1608 treated as an error.
- Studio tests passed all 1,941 cases on each of .NET 8, 10 and 11.
- Desktop tests passed all 11 cases on .NET 11.
- The final .NET 11 web distribution published and launched successfully.
- Browser checks covered node creation, dragging, port wiring, deletion, pan,
  zoom, host boundary resizing, service movement, and Undo/Redo. Saved wiring
  also survived reopening in the final publication and link deletion/Undo.
- Workflow and Environment canvases reopened correctly. The resized host,
  service position and binding survived; the saved topology file was
  byte-for-byte identical after reopening and saving again.
- The final browser recorded no errors. Original Core source hashes remained
  unchanged; WFENG, user settings and existing workflows were not edited.

The full build still reports NU1903 for the existing
`Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.13 dependency. That
separate dependency advisory was not changed or suppressed in this canvas fix.

### Compatibility fix review

No additional reproducible defect or unnecessary abstraction was found. No
implementation changes were made during this review. The local project is
limited to the upstream UI layer; the diagram engine, saved schema and history
implementation remain unchanged. Its distinct internal package ID is necessary
to keep multitarget NuGet resolution separate from the original package.

The Release solution rebuilt with NU1608 treated as an error. Studio tests passed
all 1,941 cases per target and desktop tests all 11 cases per target on .NET 8,
10 and 11. This reran configuration, canvas editing, topology and Undo/Redo
coverage. The browser interaction trial recorded above was not repeated in this
follow-up; this pass focused on build, publication and asset integrity.

Fresh desktop publications succeeded for .NET 8/10/11 and a fresh browser
publication succeeded for .NET 11. All four publications contain the six original
canvas JavaScript/CSS files byte-for-byte. Generated host stylesheets reference
the correct published scoped-CSS bundle, including the different internal bundle
name on .NET 11. Original and compatibility assemblies both retain
`Blazor.Diagrams, Version=3.0.4.1, Culture=neutral, PublicKeyToken=null`.

An isolated published web companion served the new browser publication. All
seven canvas assets and the application stylesheet returned HTTP 200, with
appropriate CSS/JavaScript content types and bodies matching the publication.
The temporary host was stopped afterward. All 50 imported upstream files still
match the original archive; Core hashes are unchanged. No saved workflows,
configuration, user tabs or WFENG files were changed.

## Dependency security warning resolution

Updated only the .NET 10 `Microsoft.AspNetCore.Components.WebAssembly.Server`
pin from 10.0.13 to 10.0.14, the first patched version listed in
[GHSA-g27p-qf6j-c96c](https://github.com/advisories/GHSA-g27p-qf6j-c96c).
The advisory concerns expression evaluation in the WebAssembly debugger.
The .NET 8 pin remains 8.0.32 and the .NET 11 override remains 11.0.0.
No warning suppression or unrelated package update was added.

The patched package raises its transitive WebAssembly dependencies to 10.0.14.
The Studio test project's direct dependency-injection reference at 10.0.13 then
caused NU1605. A .NET 10-only `VersionOverride` in that test project aligns it to
10.0.14, without changing Core or plugin dependency pins. These two project/build
files are the only implementation changes.

Verification:

- Release solution build passed with NU1608 and NU1901-NU1904 treated as errors.
  Existing analyzer warnings remain, but no dependency security warning remains.
- `dotnet list Code/FluxFlow.sln package --vulnerable --include-transitive
  --format json --no-restore` reported no known vulnerable packages across 102
  projects and no audit problems.
- Studio tests: 1,941 passed per target on .NET 8/10/11, including saved
  configuration and Undo/Redo coverage.
- Desktop tests: 11 passed per target; installed-plugin tests: 7 passed per
  target, including removal/reinstall and saved-project/edit-history preservation.
- Fresh web companion and Windows desktop publications passed for .NET 8/10/11;
  the .NET 11 browser published successfully. Published dependency manifests
  contain exactly the intended Server version for each target.
- Each published web companion launched with an isolated copy of nine existing
  plugin packages. Catalogs matched the previous publication exactly, all 16
  portable design assembly responses matched installed files byte-for-byte, and
  browser HTML/canvas assets were served unchanged.
- Each published web host completed two Scheduler/Gate/Mapper/Logger runs with
  output, original log text, Stop retention and rerun isolation verified. These
  checks used API workflow Stop; subsequent process termination is not evidence
  of graceful host shutdown. Desktop applications were republished and tested,
  but not interactively relaunched in this security patch pass.

Temporary hosts were stopped. Source hashes confirm no Core, plugin or other
runtime source edits; WFENG, saved settings and user workflows were untouched.

## Published browser acceptance

The patched .NET 11 publication passed an interactive browser trial with a
temporary project and isolated copy of the nine existing plugin packages. User
tabs, installed plugins and saved settings were not edited.

- Loaded Scheduler → Gate → JSONata Mapper → Logger; edited the interval and
  verified Undo/Redo restored both values. Added a component and checked its
  Undo/Redo, moved a wired component, deleted a connection and reconnected it.
- Ran and stopped twice from Studio. Expected output remained visible after
  Stop and stable while stopped. The second run contained none of the first
  run's message IDs.
- Saved and reopened the edited project. Removed Routing through Plugins,
  observed Pending removal, and restarted only the temporary companion.
- Opened and saved the project while Routing was absent. Studio reported that
  the Gate and unavailable link declarations were preserved. Every saved JSON
  section matched the pre-removal snapshot.
- Reinstalled the original local Routing package through the file picker,
  observed Pending install, restarted the temporary companion, and reopened
  the missing-plugin snapshot. The Gate editor and wiring returned. Property
  Undo/Redo worked again, and the restored workflow ran and stopped successfully.
- The final saved project remained JSON-equivalent to the pre-removal snapshot.
  Browser error logs were empty, as were all temporary companion stderr files.

No implementation fix was necessary. The temporary host was stopped after the
trial. Core and WFENG remained untouched.

## Published desktop acceptance

The patched .NET 11 Windows desktop publication passed an interactive trial
using a temporary Scheduler/Gate/JSONata Mapper/Logger project and an isolated
copy of the nine existing plugin packages.

- Opened the project through the native file picker. Changed Gate's Initially
  open value, verified Undo and Redo, then restored the original value.
- Saved through native Save As, closed and reopened Studio, opened the saved
  project, and used normal Save. Both saves were JSON-equivalent to the original
  project, including configuration, environment, node positions and wiring.
- Ran and stopped from the toolbar. Cleanup completed, the UI returned to
  Stopped, and the 21 retained log records stayed available while stopped.
  Rerun started a fresh observation set with new timestamps and expected output.
- Closed the native window during active execution in both desktop sessions.
  Both logs confirmed execution stopped and resource cleanup completed before
  host shutdown. The second session's captured process exit code was 0; the
  first launch monitor did not retain an exit code. Neither session wrote stderr
  or displayed a cleanup-error dialog.
- After the first active-run close, the reopened saved project ran successfully
  again. No previous run remained active. Both temporary desktop processes exited.
- All 209 files in the isolated plugin store remained byte-identical to their
  source copy. No Studio settings file was created; user workflows, tabs and
  installed plugins were not changed.

No implementation changes were needed. This trial used local in-process
components, not external brokers or hardware. Core and WFENG were untouched.
