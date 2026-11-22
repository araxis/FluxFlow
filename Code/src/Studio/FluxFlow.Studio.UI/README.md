# FluxFlow.Studio.UI

A shared Razor class library and MudBlazor renderer over the headless
authoring library (`FluxFlow.Authoring`). It uses DI-registered component
descriptions and preserves engine configuration; it owns no resources and no runtime.

## UI organization

Keep feature markup, code-behind, styles and JavaScript together. Observations lives
in `Features/Observations`, with `Logs`, `Traces`, `Output` and shared `Details`
subfolders. Logs' scrolling script is collocated as `ExecutionLogs.razor.js`.

Reusable presentation controls live in `Components`:

- `Grid/StudioDataGrid` inherits MudDataGrid's existing API and supplies compact
  spacing, smaller header icons, resizable columns and column-menu filter defaults.
  Record views enable filtering where appropriate; filters never rewrite records.
- `Grid/StudioPropertyGrid` provides the shared two-column inline property editor.
  Its styles live beside the component. Property editing is not record filtering.
- `Tabs/StudioTabBar` places tabs and an optional toolbar in one row, allowing the
  toolbar to wrap and the tabs to scroll at narrow widths. The caller owns selection.

Use these shared controls instead of copying their markup or CSS into a feature.
The Trace waterfall retains its hierarchy and timing renderer; arbitrary row sorting
would break that hierarchy. Docking and closable workflow tabs retain their own
interaction behavior. Presentation state does not enter configuration or Undo/Redo.

## Host integration

Reference this library and call `services.AddStudioUI()`. The host registers scoped
implementations of `IStudioFileAccess`, `IStudioSettingsStorage` and the existing
`IExpressionEvaluator`, `IProposalGenerator` and `IProposalConnectionService`. Use a separate DI scope for each editor session/window;
the graph, Undo/Redo history and preferences are scoped services. Render
`Features.Designer.Pages.DesignerPage` directly or include its assembly in the
host's Blazor Router. The shared components do not select a web render mode.
Recreating the page within that scope retains the graph's Undo/Redo history,
open/closed project state and host file identity. Diagram widgets register once
per graph. Panel layout and open-tab presentation are recreated with the page.

`IStudioFileAccess` returns file content and a display name, or null when a picker
is cancelled. An optional `StudioFile.Location` is interpreted only by the host,
so a desktop implementation can retain a native file path for Save. Save As asks
for a new destination. The editor retains the returned location only after a
successful open/save. File failures and cancellation do not reset its history.
Import still supports pasted JSON and an application section; Open project uses
the host file picker directly. Both Save and Export use the same host boundary.

Chat's paperclip menu offers JSON/text files and PNG/JPEG images. Images appear as
removable thumbnail chips (two images, 2 MB each). `IStudioFileAccess.OpenImageAsync`
returns `StudioImageFile` with a display name, media type and base64 contents, or null
on cancellation. A desktop host uses its native picker behind the same boundary.
Attachments stay in the conversation; they do not enter project configuration,
settings or Undo/Redo history. Sending uses captured contents, independent of later
picker results or draft changes. Image requests require the selected local model
to report vision support; they never download a model.

`IStudioSettingsStorage` reads/writes the existing settings JSON. Validation,
autosave notifications and serialization stay in `StudioPreferences`. A failed
write leaves the previous in-memory settings intact. The browser implementations
and HTTP evaluation adapter live in `FluxFlow.DesignerApp/Services`.

Hosts include the MudBlazor, BlazorMonaco and diagram library assets, the host's
generated `.styles.css` bundle, and these shared static assets:

- `_content/FluxFlow.Studio.UI/css/studio.css`
- `_content/FluxFlow.Studio.UI/Features/Designer/Expressions/jsonata-language.js`

See `FluxFlow.DesignerApp/wwwroot/index.html` for script order and Monaco startup.
UI JavaScript interop remains asynchronous and confined to the existing controls;
browser file-picker/download interop lives only in the browser host. The Windows
[desktop host](../FluxFlow.Studio.Desktop/README.md) uses WPF BlazorWebView, native
file dialogs and local settings storage. A MAUI host is not included yet.

## Shared editing batches

`DesignerGraphState.PrepareEdits` uses the headless configuration batch API described
in [Authoring](../../Authoring/FluxFlow.Authoring/README.md). It returns a session-bound
proposal containing candidate configuration, revision and registration diagnostics.
It also checks connection direction against the registered component descriptions.
Preparation leaves the live design unchanged; completed manual edits awaiting a
checkpoint are recorded first. Validation diagnostics may describe incomplete drafts
or checks that require the runner; preparation is not permission to execute anything.

Call `ApplyEdits(proposal)` on the same UI dispatcher as the canvas. The accepted
batch appears in the live graph as one change and one Undo/Redo entry, retaining
unrelated configuration, existing positions/colors, link sides and file identity.
A no-op does not discard Redo. File saving and settings autosave remain separate.
If canvas projection fails, rollback also retains the current workflow and selection,
including navigation performed since the last Undo checkpoint.

A manual design edit, Undo/Redo, or document replacement invalidates pending
proposals. Even editing then undoing back to the old content requires preparation
again. Selection, workflow navigation and zoom do not invalidate them. Application
checks for mutations still waiting for the normal canvas/Inspector checkpoint.
There is no automatic merge. Proposals cannot be applied to another editor session.

Unfinished Inspector edits, expression/text dialogs and canvas gestures block
application until completed or cancelled. Shared controls use `BeginManualEdit()`;
custom controls with unsaved drafts should hold the returned IDisposable for the
same lifetime. Cancelling a draft without changing the design keeps a proposal valid.
**Open chat** in the toolbar or **View → Show chat** opens the shared dockable Chat
panel beside the Inspector. Ask about the active workflow or describe changes.
Ask mode shows Apply/Discard inside the conversation; Automatic applies each
validated batch immediately. Expand Configuration details to inspect the JSON.
The shared [conversation feature](../../Authoring/FluxFlow.Conversations/README.md) uses
the configuration snapshot and registered descriptions to propose add/configure/connect
operations. Existing names are never replaced by an add operation.
Each batch is one Undo/Redo action; Discard stops the current request without
reverting completed batches. Apply does not run
the workflow or save the project file. Manual edits invalidate the pending preview;
discard it and prepare again to include those edits. Navigation leaves its original
target workflow unchanged. Changes made while generation is running also reject the
response, including an edit followed by Undo. Discard cancels the pending request;
late responses cannot restore a discarded proposal. Pending previews and request
text are page state and are not saved. Follow-up requests include complete recent
turns within the provider's existing request limit, alongside a fresh configuration
snapshot. Questions are answered from configuration and descriptions, without
inventing runtime observations. Editing supports add/configure/connect operations;
workflow execution remains separate. Cancel request stops further steps;
New chat cancels and clears the conversation. Opening, replacing or closing a project
also clears it. Docking, hiding panels and visiting settings retain the conversation.
Closing the editor cancels outstanding generation. Each request captures its current
connection preferences; the host still owns the connection and provider transport.

## What It Shows

- **Component palette** - registered descriptions, grouped by
  category and labeled with display name and component type.
- **Inspector** - option sections in metadata order, host-selected editors, and
  host-owned resource picker prompts.
- **Canvas** - centered colored port rows, with connection handles on both sides.
  Error is a filtered Output, not a separate runtime port.

## Adding a Studio component

Each built-in component has a folder under `FluxFlow.Authoring/Definitions`:
Scheduler, Source, JsonataMapper, Batch or Gate. Its `IComponentDescription` supplies the
palette title, category, icon, default color, summary, ports and Inspector fields.
`NodeInspectorModel.PrimaryEditor` names the field opened on double-click;
omit it when there is no main editor. A port's `ColorRole` selects the existing
Input, Output, Events or Error settings, independently of the port's name.
Omitting the role uses its direction. Port metadata also has summary and input
shape fields for component documentation.

Keep descriptions independent of Razor, add one in its own folder, and register it in the host:

```csharp
services.AddSingleton<IComponentDescription, MyComponentDescription>();
```

`AddStudioUI()` calls the headless `AddDesignerComponents()` registration for the built-in components. The graph receives
`IEnumerable<IComponentDescription>` directly; the palette, canvas and Inspector
reuse these descriptions and shared Razor components. There is no scanning or
separate registry. Register one description per runtime component type. Runtime
configuration registration is separate and belongs to the host/runner.
Headless discovery and workflow inspection use these same descriptions; see the
[Authoring guide](../../Authoring/FluxFlow.Authoring/README.md#discovery-and-workflow-inspection).

Use the existing `OptionEditorModel.Create` helper to declare ordinary fields with
explicit display labels, for example:

```csharp
Create(nameof(BatchConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Number,
    "Maximum number of batches buffered at the output. Must be greater than zero.", defaultValue: 128)
```

The first argument is the configuration key; the second is the readable UI label.
Labels are written in each description, not inferred from property names.
Help text appears below the Inspector when the property is clicked or focused.
`OptionEditorKind.Json` edits actual JSON values (including strings, arrays and objects),
validates syntax before saving, and retains nested property spelling. Blank text removes
the optional setting; JSON `null` remains an explicit null. Use `MultilineText` for
settings whose runtime contract is a string containing JSON or an expression/file path.
`DefaultValue` describes the registration default and is displayed for an omitted
setting without writing it to configuration. Explicit values, including null, are
preserved. Required settings without a valid default stay empty; use `IsRequired`
and help text to describe them.
Clearing a text or number override removes that setting; its default remains visible
as a placeholder. Explicit null values do not receive a default placeholder.
The shared editors preserve property spelling and write through the existing
configuration and Undo/Redo path. `InitializeConfiguration` is called only for
new nodes; imported configuration never receives preset defaults. JSONata Mapper
sets Engine to `jsonata` for a new node. Its description alone handles imported
custom or missing engines, retaining the generic Mapper title and Engine field.

These descriptions and Razor components are shared UI code without browser or
desktop services. A desktop host can register the same descriptions.

## Run

```sh
./eng/run-studio.ps1
```

Run this from `Code`, then open `http://127.0.0.1:5289`. The script publishes the
UI and starts the thin web host, including the expression evaluation endpoint.

## Canvas

`Features/Designer/Canvas/` adds a Z.Blazor.Diagrams node canvas:

- Clicking a palette component adds a `FlowNodeModel` with fixed metadata ports.
- Selecting a node drives the inspector for that component type.
- `DesignerGraphState` owns the diagram, active workflow, preserved configuration
  document, and current selection.
- Loaded link models retain their input-side or output-side declaration and
  optional condition text.
- Zoom-to-fit and clear commands stay host UI concerns.

## Persistence

The File menu separates Studio projects from engine configuration:

- **Open project / Save project** reads or writes a `.fluxflow` JSON file with
  the full configuration, application section, active workflow, node positions
  and colors, link attachment sides, and project port-color overrides.
- **Import engine configuration** starts a project from plain engine JSON,
  optionally inside a host configuration section, with default editor layout.
- **Export engine configuration** writes only the selected application section,
  including resources and workflows. Studio metadata and unrelated host sections
  are excluded.
- **Publish application…** packages that same export, the .NET 11 Runner, its
  installer and the required installed plugins in a ZIP. Choose a Run environment
  with one application and manual connections. On the destination computer,
  install PowerShell 7 and the ASP.NET Core 11 runtime, supply the environment
  variables and external services used by the project, and extract the ZIP.
  For each relative Source path, select the input file to bundle. For example,
  `file:data/orders.json` places the selected bytes at `runner/data/orders.json`.
  Repeated references share one copy. Absolute paths still refer to files on
  the destination computer. Run `pwsh -File ./start.ps1` from the extracted
  folder and press Enter to stop.
  Each launch uses a private plugin store and leaves its Runner logs under `logs/`.

Save downloads a project file in the web version. Its filename is editable;
the browser controls the destination and does not automatically overwrite an
existing local file. Export is for the engine and does not replace saving the
Studio project. Unsupported project versions and metadata fields are rejected
rather than silently discarded. Configuration property casing and unknown
component settings are preserved.

Referenced external files are not bundled. Keep them with the configuration and
provide the appropriate base directory to the runner. Docking layout, open-tab
lists and Undo/Redo history are session state; the active workflow and canvas
geometry are saved.

## Settings

**Edit → Studio settings** opens an application page with Appearance, Canvas and Connection
categories. The designer stays mounted while hidden, preserving its workflow tabs,
selection, layout and Undo/Redo history. Changes save automatically without leaving
the page. The bottom status bar shows Saving, Saved, or a save failure; Back returns
to the designer. Theme and
default Input, Output, Events and Error colors persist through the host's settings
storage. The web client uses the existing browser local-storage key.
Connection stores the local endpoint and selected installed model in the same
preferences. Refresh models lists installed models; Test connection checks that
the selected model responds. Neither action changes configuration or history.
If a connection change cannot be saved, the draft remains visible with an error;
Refresh and Test stay disabled until the draft saves successfully.
An unset connection inherits host defaults. Desktop hosts implement the same
`IProposalConnectionService` boundary; browser transport stays in the web client.
**File → Project settings** applies per-project color overrides immediately,
with Undo/Redo. Use Save project to write those overrides into the Studio project file.

Colors resolve from project override, then Studio preference, then theme default.
Reset returns to the inherited color. Custom colors apply to both Light and Dark
themes. Ports and their outgoing links share the color; selection retains it.
Settings and the Inspector share a compact color-filled field and reset icon.
Click the field to open MudBlazor's picker; HEX editing and the color controls
are available inside the popup.
Project overrides participate in Undo/Redo; global Studio preferences do not.
Neither kind of setting enters engine configuration.

## Editing Values

Inspector editors write flat component properties into
`FlowNodeModel.Configuration`. Values round-trip through project save/load and
reappear when the node is selected. Untouched components stay minimal.
Component and link inspectors use MudSimpleTable with a shared two-column layout,
cell separators and borderless inline fields. Click or Tab into a value to edit it; keyboard focus
has a compact focus indicator without changing the cell background. Groups, property help and specialized editors
remain available. Focusing an unchanged field does not write defaults or add history.

See the [Property Grid standards](Components/Grid/README.md) and the
[configuration editor inventory](docs/configuration-editor-inventory.md) for the
configuration UI coverage and remaining harmonization work.

## Boundary

The renderer edits configuration through the shared Authoring boundary.
Registering descriptions does not create runtime components or resources.
Workflow execution and resource ownership remain with the host/runner; UI
presentation does not change Core or the engine's configuration contract.
