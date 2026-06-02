# Component Engine Boundary Rebuild

Date: 2026-06-02

## Trigger

The first external consumer confirmed that `FluxFlow.Engine` `0.6.0-beta.1`
restores correctly and targets `.NET 8`, but older published component packages
still reference the old `FluxFlow.Engine.Core.FlowNodeId` binary symbol.

That means the issue is not framework support. It is a package set mismatch:
old component DLLs can be restored beside the new engine and then fail at
runtime.

## Decision

Before publishing the stable engine tag, republish every currently published
component package from the current source tree.

The rebuild packages should:

- Depend on `FluxFlow.Engine` `1.0.0`.
- Reference `FlowNodeId` from `FluxFlow.Engine.Components`.
- Keep component behavior unchanged.
- Use patch prerelease bumps because this is a compatibility rebuild, not a new
  feature slice.

## Versions

- `FluxFlow.Components.Assertions`: `0.1.1-alpha.1`
- `FluxFlow.Components.Control`: `0.2.1-alpha.1`
- `FluxFlow.Components.FileSystem`: `0.4.1-alpha.1`
- `FluxFlow.Components.Http`: `0.1.1-alpha.1`
- `FluxFlow.Components.Mapping`: `0.1.1-alpha.1`
- `FluxFlow.Components.Metrics`: `0.1.1-alpha.1`
- `FluxFlow.Components.Mqtt`: `0.2.2-alpha.1`
- `FluxFlow.Components.Observability`: `0.1.1-alpha.1`
- `FluxFlow.Components.Payloads`: `0.1.1-alpha.1`
- `FluxFlow.Components.Routing`: `0.6.1-alpha.1`
- `FluxFlow.Components.Serialization`: `0.1.1-alpha.1`
- `FluxFlow.Components.Sessions`: `0.1.1-alpha.1`
- `FluxFlow.Components.Sources`: `0.1.1-alpha.1`
- `FluxFlow.Components.State`: `0.1.1-alpha.1`
- `FluxFlow.Components.Storage`: `0.2.1-alpha.1`
- `FluxFlow.Components.Storage.FileSystem`: `0.1.1-alpha.1`
- `FluxFlow.Components.Timers`: `0.4.2-alpha.1`
- `FluxFlow.Components.Validation`: `0.1.1-alpha.1`

## Release Order

1. Commit the component version and changelog updates.
2. Wait for branch CI.
3. Push `engine-v1.0.0`.
4. After engine publish succeeds, push the component compatibility tags.
5. Verify public restore with `FluxFlow.Engine` `1.0.0` and several rebuilt
   component packages.

## Verification

- Full solution build: passed.
- Full solution tests: passed.
- Branch CI after compatibility rebuild commit: passed (`26816952571`).
- Release alias and changelog resolution: passed for engine plus all component
  packages.
- Local package pack: passed for engine plus all eighteen component packages.
- Package dependency inspection: passed for all component packages.
- Local package-set restore/build smoke: passed with a fresh `net8.0` consumer
  referencing the local engine package plus all rebuilt component packages.
- Public package-set restore/build smoke: passed with a fresh `net8.0` consumer
  and clean package cache on restore attempt 15, after public feed indexing
  caught up.

## Release Result

- Engine tag: `engine-v1.0.0`, release workflow `26817066115`, passed.
- First component batch: seventeen package releases started through
  `workflow_dispatch`, all passed.
- Storage file-system adapter tag:
  `components-storage-filesystem-v0.1.1-alpha.1`, release workflow
  `26817442913`, passed.
- Bulk-pushing seventeen component tags in one command did not create release
  workflow runs. For future batches, either push tags one at a time or use
  `workflow_dispatch` intentionally.

## Risk

The component packages are still prerelease. The rebuild fixes the binary
namespace mismatch, but normal component maturity work remains outside the
engine v1 release gate.
