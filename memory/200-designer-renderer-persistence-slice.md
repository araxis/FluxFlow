# Designer Renderer Persistence Slice

Date: 2026-07-03

## Summary

Completed the Designer host layer renderer (docs/18 phase 5). The final slice
adds save/load of the canvas graph as a `FluxFlow.Composition` definition, with
validation feedback. All four renderer slices are now done and browser-verified:
palette, inspector, node canvas, and persistence.

## Changes

- `Features/Designer/Canvas/DesignerGraphMapper.cs` — maps the `BlazorDiagram`
  to and from the host-model `GraphModel`. Link endpoints are resolved back to
  named ports: source output ports (right-aligned) and target input ports
  (left-aligned) are matched to the node's `OutputPortNames`/`InputPortNames`
  by alignment and order, with sensible `Output`/`Input` fallbacks for nodes
  that declare no fixed ports.
- `DesignerGraphState` now takes `DesignerCatalog`, and adds `ToJson()`
  (`ToGraph` -> `GraphDefinitionMapper.ToDefinition` -> serialize with
  `CompositionDefinitionJson`), `LoadJson()` (deserialize ->
  `GraphDefinitionMapper.FromDefinition` -> `DesignerGraphMapper.Load`,
  returning `ValidationMessageModel` warnings), and `Clear()`.
- `DesignerCatalog.Find(componentType)` for palette lookup on load.
- `GraphJsonDialog` (MudBlazor) shows JSON read-only on save and editable on
  load. The `DesignerPage` toolbar gained Save / Load / Clear (plus the existing
  zoom-to-fit); load warnings and errors surface through `ISnackbar`.
- Added a JSON serialize/deserialize round-trip test to
  `tests/FluxFlow.DesignerHost.Tests/GraphDefinitionMapperTests.cs` to prove the
  save/load path (a `CompositionDefinition` survives System.Text.Json on its own,
  not only the model mappers) and guard it.

## Verification

- The new JSON round-trip test passes; full `FluxFlow.DesignerHost.Tests`
  suite `30` passed. Release convention tests `92` passed. Full `FluxFlow.sln`
  Release build clean (`0`/`0`).
- Browser round-trip via the preview tooling: added `timer.interval` and
  `storage.put` nodes; Save produced valid composition JSON with
  `workflows.main.nodes = [interval-1, putRecord-2]`; Clear emptied the canvas;
  Load restored both nodes ("Interval Timer", "Storage Put") with a "Loaded
  composition." success snackbar; no console errors.

## Boundaries and follow-on ideas

- The app remains a sample/tool (no `PackageId`), a reference host for the
  design-time model layer; it owns no resources and no runtime.
- Node option values are not yet edited into a saved node (the inspector holds
  scratch values); wiring option editors to a selected node's configuration is
  a natural next enhancement, as are drag-to-connect link validation, delete,
  and undo/redo. These are optional; the phase-5 renderer goal (render the
  host-model layer end to end with persistence) is met.
