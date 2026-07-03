# FluxFlow.DesignerApp

A Blazor WebAssembly + MudBlazor renderer over the headless Designer host-model
layer (`FluxFlow.DesignerHost`). It is the UI half of `docs/18-designer-host-layer.md`
phase 5 — it reads package-owned design metadata and renders it; it owns no
resources and no runtime.

## What it shows

- **Component palette** — every component from the metadata catalog, grouped by
  category and labelled with its display name and node type. Built from
  `DesignerHostCatalog.CreatePaletteItems()`.
- **Inspector** — for the selected component: option sections in metadata order
  (primary options before advanced ones), each option rendered by the MudBlazor
  editor its `OptionEditorKind` resolves to (text, number, toggle, select,
  secret, multiline, expression, JSON), plus the host-owned resource picker
  prompts (picker kind, key pattern, value type, required). Built from
  `DesignerHostCatalog.CreateInspector(...)`.

The catalog is assembled in `Features/Designer/DesignerCatalog.cs` from a
representative set of package metadata providers; adding a component family is a
one-line provider addition.

## Run

```sh
dotnet run --project samples/FluxFlow.DesignerApp --launch-profile http
```

Then open the printed `http://localhost:5298` URL.

## Canvas

`Features/Designer/Canvas/` adds a Z.Blazor.Diagrams node canvas:

- Clicking a palette component adds a `FlowNodeModel` to the canvas with
  input/output ports (left/right) derived from the component's port metadata.
- Selecting a node on the canvas drives the inspector for that component type.
- `DesignerGraphState` owns the single `BlazorDiagram` and the current
  selection; the page reads that state and never touches the diagram directly.
- Zoom-to-fit toolbar action; empty-state prompt over the canvas.

## Persistence

The canvas toolbar saves and loads the graph as a `FluxFlow.Composition`
definition:

- **Save** shows the graph serialized to composition JSON.
  `DesignerGraphMapper.ToGraph` reads the canvas nodes and links (resolving link
  endpoints back to named ports), then `GraphDefinitionMapper.ToDefinition`
  produces the `CompositionDefinition`.
- **Load** deserializes composition JSON and rebuilds the canvas via
  `GraphDefinitionMapper.FromDefinition` + `DesignerGraphMapper.Load`. Unknown
  component types or ports surface as `ValidationMessageModel` warnings shown in
  a snackbar; a hard JSON error shows an error snackbar.
- **Clear** empties the canvas.

## Status

The full renderer is in place and browser-verified: component palette,
option/resource inspector, node canvas (add-from-palette, select-to-inspect),
and save/load persistence to composition JSON with validation feedback — all
driven by the real metadata catalog and the `FluxFlow.DesignerHost` model layer.
