# Designer Renderer Option Editing

Date: 2026-07-03

## Summary

Made the Designer renderer app produce meaningfully-configured compositions:
the inspector's option editors now write into the selected canvas node's
configuration, so saved composition JSON carries real option values that
round-trip through save/load. This closes the last functional gap in the
`samples/FluxFlow.DesignerApp` reference host.

## Changes

- `FlowNodeModel` gained a mutable `Configuration` dictionary
  (`Dictionary<string, JsonElement>`), the per-node option values.
- `OptionEditorField` takes the node's `Configuration` and binds through it:
  `OnInitialized` seeds the editor from any stored value (so values show after a
  load), and each editor writes back on change via `@bind-Value:after`
  (text/select/secret/multiline as strings, number as double, toggle as bool).
  Writes happen only on user change, so untouched options stay absent from the
  configuration; clearing a text/number value removes the key.
- `ComponentInspector` forwards `Configuration` and a `NodeKey` (the selected
  node name); the option editors are `@key`-ed by `(NodeKey, option.Name)` so
  switching nodes gives fresh editors seeded from the newly selected node.
- `DesignerPage` passes `Graph.SelectedNode?.Configuration` and `?.NodeName`.
- `DesignerGraphMapper.ToGraph` emits `GraphNodeModel.Options` from the node
  configuration; `Load` seeds each rebuilt node's `Configuration` from the
  definition's node options.

## Verification

- `FluxFlow.DesignerApp` Debug and full `FluxFlow.sln` Release builds clean
  (`0`/`0`).
- Browser round-trip via the preview tooling: added a `timer.interval` node,
  selected it, set the `Interval` field to `00:00:05`; Save produced JSON with
  `nodes.interval-1.configuration = { "interval": "00:00:05" }`; Clear + Load
  restored the node and re-selecting it showed the `Interval` field back at
  `00:00:05`. No console errors.

## Status

All Designer host layer phases that were in scope are done: host-model layer
(1), catalog projection (2), persistence mapping (4), and the renderer app (5)
— palette, editable inspector, canvas, and save/load. Phase 3 (a host
resource-catalog adapter binding picker hints to real keyed resources) needs a
real host app and is out of the sample's scope. Optional editor polish
(drag-to-connect link validation, delete, undo/redo) remains a nice-to-have.
