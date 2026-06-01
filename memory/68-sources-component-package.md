# Sources Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Sources` as the first deterministic source package.

The package starts with:

- `source.generated`
- `source.sequence`

Generic replay is deferred. `FluxFlow.Components.Sessions` already owns stored
session replay, and a second replay shape should wait until a separate neutral
recording format is proven.

## Package Shape

`source.generated`:

- emits configured JSON items as a host-registered output type
- supports object, JSON, primitive, byte array, and custom aliases
- supports `loop` with required `maxItems`
- supports `initialDelayMilliseconds` and `intervalMilliseconds`
- exposes `Output` and `Errors`

`source.sequence`:

- emits `SourceSequenceItem`
- supports `start`, `step`, and `count`
- supports `initialDelayMilliseconds` and `intervalMilliseconds`
- exposes `Output` and `Errors`

Both nodes:

- use bounded output capacity
- complete output when the configured stream finishes
- stop cleanly when `Complete` is called
- emit diagnostics for start, item emission, completion, and failure

## Boundary

The package does not know about any transport envelope, workspace schema,
stored session repository, scenario runner, or dashboard model.

Applications should keep app-specific source defaults and source adapters in the
host, then use this package for deterministic generated streams that are useful
across products.

## Verification Plan

- Run focused Sources tests.
- Run solution tests.
- Pack the Sources package.
- Resolve the package release tag from the release manifest.
