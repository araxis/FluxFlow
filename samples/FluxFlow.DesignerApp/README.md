# FluxFlow.DesignerApp

A Blazor WebAssembly and MudBlazor renderer over the headless Designer
host-model layer (`FluxFlow.DesignerHost`). It reads package-owned design
metadata and canonical application models; it owns no resources and no runtime.

## What It Shows

- **Component palette** - components from the metadata catalog, grouped by
  category and labeled with display name and component type.
- **Inspector** - option sections in metadata order, host-selected editors, and
  host-owned resource picker prompts.
- **Canvas** - typed inputs on the left, payload-independent signal inputs on
  the top, and outputs on the right.

The catalog is assembled in
`Features/Designer/DesignerCatalog.cs` from a representative set of package
component declarations registered by the matching Composition family
extensions.

## Run

```sh
dotnet run --project samples/FluxFlow.DesignerApp --launch-profile http
```

Then open the printed `http://localhost:5298` URL.

## Canvas

`Features/Designer/Canvas/` adds a Z.Blazor.Diagrams node canvas:

- Clicking a palette component adds a `FlowNodeModel` with fixed metadata ports.
- Selecting a node drives the inspector for that component type.
- `DesignerGraphState` owns the diagram, active workflow, loaded canonical
  document, and current selection.
- Loaded link models retain their input-side or output-side declaration and
  optional condition text.
- Zoom-to-fit and clear commands stay host UI concerns.

## Persistence

The toolbar saves and loads the canonical flat
`FluxFlow.Composition.Model.ApplicationDefinition`:

- **Save** reads canvas nodes and links into a `DesignerWorkflow`, preserves
  resources, other workflows, non-rendered links, and loaded declaration sides,
  then writes `Resources` / `Workflows` JSON through
  `DesignerApplicationPersistence`.
- **Load** projects canonical JSON through `DesignerApplicationPersistence` and
  rebuilds the active workflow canvas. Runtime link diagnostics plus unknown
  component or port warnings appear through `ValidationMessageModel`; hard JSON
  errors appear in an error snackbar.
- **Clear** empties the active canvas without introducing another persistence
  schema.

## Editing Values

Inspector editors write flat component properties into
`FlowNodeModel.Configuration`. Values round-trip through canonical save/load and
reappear when the node is selected. Untouched components stay minimal.

## Boundary

The renderer uses real metadata and canonical Composition persistence. It does
not create resources, execute workflows, own service providers, implement hot
reload, or depend on `FluxFlow.Engine` or transport adapters beyond the
component metadata packages explicitly included in the sample catalog.
