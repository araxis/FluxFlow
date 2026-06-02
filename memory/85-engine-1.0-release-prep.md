# Engine 1.0 Release Prep

Date: 2026-06-02

## Target

Prepare `FluxFlow.Engine` `1.0.0`.

The first consumer migrated to `0.6.0-beta.1` successfully, so the beta boundary
is ready to be promoted to a stable engine release.

## Included Changes

- Bump `FluxFlow.Engine` to `1.0.0`.
- Add `FluxFlow.Engine 1.0.0` changelog notes.
- Add public API overview docs.
- Add engine compatibility docs.
- Add migration guidance from `0.5.0-alpha.1` to the beta/stable boundary.
- Record first consumer beta adoption success.

## Release Inputs

- Project version: `1.0.0`.
- Release tag: `engine-v1.0.0`.
- Changelog section: `FluxFlow.Engine 1.0.0`.

## Verification

- Full solution build: passed.
- Full solution tests: passed.
- Sample app run: passed.
- Release-note extraction: passed.
- Local package pack: passed.
- Local package install smoke test: passed.
- Branch CI after release-prep commit: passed (`26814635047`).
- Release workflow after tag.
- Public package restore smoke test.

The package smoke restored `FluxFlow.Engine` `1.0.0` from the local package
output and compiled a minimal consumer against:

- `ApplicationDefinition`
- `RuntimeNodeFactoryRegistry`
- `FlowNodeId` from `FluxFlow.Engine.Components`

## Next Step

Publish `FluxFlow.Engine` `1.0.0` only after the component compatibility
rebuild commit is also green. Then publish the rebuilt component packages so
consumers do not mix the stable engine boundary with old component binaries.
