# Designer Renderer Canvas Slice

Date: 2026-07-03

## Summary

Added the node canvas to `samples/FluxFlow.DesignerApp` (Designer host layer
renderer, docs/18 phase 5), the second renderer slice. Z.Blazor.Diagrams renders
graph nodes with ports; clicking a palette component adds a node; selecting a
canvas node drives the option/resource inspector. Browser-verified end to end.

## Library and API verification

- `Z.Blazor.Diagrams` `3.0.4.1` (and `Z.Blazor.Diagrams.Core` `3.0.4.1`),
  current stable, net10.0.
- Verified the exact 3.x API by reflecting over the packaged assemblies (no XML
  docs shipped): `NodeModel(Point)` with settable `Title` and
  `AddPort(PortAlignment)`; `PortAlignment` values `Top/Right/Bottom/Left`
  (plus corners); `LinkModel(PortModel, PortModel)`; `Diagram.Nodes.Add(node)`
  returns the node; `Diagram.SelectionChanged` is `Action<SelectableModel>`;
  `Diagram.GetSelectedModels()`; `Diagram.ZoomToFit(margin)`. Confirmed static
  assets in the package: `_content/Z.Blazor.Diagrams/style.min.css`,
  `default.styles.min.css`, `script.min.js`. The main-package types
  (`BlazorDiagram`, `DiagramCanvas`, `CascadingValue`-fed canvas) were verified
  by a clean compile.

## Structure

`Features/Designer/Canvas/`:
- `FlowNodeModel : NodeModel` — carries `ComponentType`, `NodeName`, and the
  input/output port-name lists (kept for the persistence slice); places input
  ports left, output ports right, falling back to one of each so every node is
  connectable.
- `DesignerGraphState` (scoped) — owns the single `BlazorDiagram` and the
  current `SelectedNode`; raises `Changed` on selection so the page refreshes
  the inspector. The UI reads this state and never mutates the diagram directly.

`DesignerPage` became a three-pane layout (palette | canvas | inspector) with a
`.razor` + `.razor.cs` code-behind: palette click -> `AddNode`; canvas selection
-> inspector; a zoom-to-fit toolbar action and an empty-state overlay. Static
assets and `@using Blazor.Diagrams.Components` were added.

## Verification

- `FluxFlow.DesignerApp` Debug build and the full `FluxFlow.sln` Release build
  passed with `0` warnings and `0` errors.
- Ran the app and drove it in the browser via the preview tooling: clicking the
  `timer.interval` and `storage.put` palette items added two nodes rendered on
  the canvas with their display-name titles; dispatching a pointer-down/up on
  the "Interval Timer" node selected it and the inspector switched to
  `timer.interval` (empty state cleared). No console errors.

## Follow-on

- Persistence slice (task): map the canvas graph to/from `CompositionDefinition`
  via `GraphDefinitionMapper` (named-port links), save/load JSON, and show
  validation via `ValidationMessageModel`. `FlowNodeModel` already keeps the
  input/output port names needed for the named-link mapping.
