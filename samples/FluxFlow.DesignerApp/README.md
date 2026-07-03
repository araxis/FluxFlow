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

## Status

First slice: palette + inspector, driven end to end by the real metadata
catalog. The node canvas (Z.Blazor.Diagrams), graph persistence to
`FluxFlow.Composition` definitions via `GraphDefinitionMapper`, and validation
display are the follow-on slices.
