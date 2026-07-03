# Designer Host Layer Planning

Date: 2026-07-03

## Summary

Recorded a documentation-only plan for a future Designer host layer outside
component packages. The plan turns the published Designer metadata and resource
picker hint contracts into host responsibilities without adding package source,
public APIs, renderer behavior, runtime behavior, release tags, or publishing
work.

## Changes

- Added `docs/18-designer-host-layer.md`.
- Linked the new page from `docs/README.md`.
- Updated `docs/17-component-coverage-matrix.md` so the Designer host layer is
  now planned and any host prototype is a separate future pass.
- Defined the host boundary around `ComponentDesignMetadataCatalog`, option
  metadata hints, resource metadata attributes, and
  `ComponentResourcePickerHints.Create(...)`.
- Documented host-owned responsibilities for palette models, node inspectors,
  option editor mapping, resource picker catalog binding, validation/status
  display, persistence mapping, and runtime adapter mapping.

## Boundaries

No source APIs, package versions, release notes, changelog entries, public API
baselines, resource ownership, renderer behavior, hot reload, runtime lifecycle
hooks, tags, or publishing state changed.

## Verification

- Release tests passed: `92` passed, `0` failed, `0` skipped.
- The first controlled Debug solution build attempt hit generated assembly file
  locks under `obj/`.
- `dotnet build-server shutdown` stopped the MSBuild and compiler servers.
- The controlled Debug solution build then passed with `0` warnings and `0`
  errors.
- `graphify update . --force` refreshed `graphify-out/`: `12447` nodes,
  `22705` edges, and `976` communities. `graph.html` was skipped because the
  graph exceeds the local HTML visualization limit.
