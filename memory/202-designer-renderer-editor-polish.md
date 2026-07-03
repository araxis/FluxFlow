# Designer Renderer Editor Polish

Date: 2026-07-03

## Summary

Added the two most essential editor operations to the Designer renderer canvas
(`samples/FluxFlow.DesignerApp`): delete selected node(s)/link(s), and
link-creation validation. Browser-verified.

## Changes

- `DesignerGraphState`:
  - `DeleteSelected()` removes the selected links then nodes
    (`Diagram.GetSelectedModels()` filtered to `BaseLinkModel`/`NodeModel`;
    removing a node removes its attached links automatically).
  - `HasSelection` gates the Delete toolbar button.
  - Subscribes to `Diagram.Links.Added` and, per new link, to
    `link.TargetAttached`. On attach, `IsValidConnection` rejects a link when it
    connects a node to itself or does not go output-port (right) -> input-port
    (left); the link is removed and a `LinkRejected` event carries the reason.
- `DesignerPage`: a Delete toolbar button (disabled unless `HasSelection`), and
  an `ISnackbar` warning surfaced from `LinkRejected`. The Delete key also works
  via Z.Blazor.Diagrams' default keyboard behavior (gated by the diagram's
  `Constraints.ShouldDeleteNode/Link`).

## API verification

- Reflected over `Z.Blazor.Diagrams.Core` `3.0.4.1`: layers expose
  `Added`/`Removed` events; `BaseLinkModel` has `Source`/`Target` (`Anchor`) and
  a `TargetAttached` event; `SinglePortAnchor.Port` gives the port;
  `DiagramConstraintsOptions` has `ShouldDeleteNode/Link/Group` (so the default
  keyboard behavior already deletes selected models).

## Verification

- `FluxFlow.DesignerApp` Debug and full `FluxFlow.sln` Release builds clean
  (`0`/`0`).
- Browser-verified via the preview tooling:
  - Delete: added a node, selected it (Delete button enabled), clicked Delete;
    node count went 1 -> 0.
  - Valid link: dragged Interval Timer output -> Storage Put input; Save showed
    `links: [{ from: interval-2.Output, to: putRecord-3.Input }]`, no rejection.
  - Invalid link: dragged output -> output; rejected with the snackbar "Links
    must go from an output port (right) to an input port (left)." and Save still
    showed 1 link (the invalid link was not persisted).
  - No console errors.

## Status

The Designer renderer now supports add, connect (validated), configure, select,
delete, and save/load. Remaining optional polish: undo/redo and duplicate. This
work is on branch `work/designer-editor-polish` (based on `main` after PR #55
merged).
