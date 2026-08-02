# Runtime And Component Review Fixes

Date: 2026-07-09

## Summary

Closed the eight repository review findings across Composition, Engine, Nodes,
FileSystem, Timers, Routing, and HTTP. The work remains local on
`fix/runtime-component-reliability`; no tags, packages, branches, pull requests,
or releases were pushed.

## Changes

- Composition data links no longer independently propagate completion into a
  shared input. One runtime-owned coordinator completes the input after all
  upstreams succeed or faults it on the first upstream failure.
- Composition runtime disposal attempts every node, graph link, and diagnostic
  link, then aggregates cleanup failures without duplicating runtime completion
  faults.
- Engine internal fanout queues are bounded to 256 pending items. Accepted
  diagnostics remain ordered; overflow is rejected immediately through the
  existing boolean result.
- Application runtime, workflow, default engine node, base engine node, and
  standalone source startup paths honor pre-canceled tokens. A canceled
  standalone source start does not consume its one-start state.
- FileSystem confined relative paths reject existing descendant symbolic links
  and reparse points. Limited file reads stream at most `maxBytes + 1` bytes.
- Debounce and timed-window callbacks emit claimed state while holding their
  lifecycle gate, preventing concurrent completion from dropping it or causing
  a post-completion write.
- HTTP textual response decoding honors supported declared charsets, including
  quoted values, and falls back to UTF-8 for missing or invalid charset data.
- Package versions moved to Nodes `1.2.1`, Engine `2.0.3`, Composition `1.2.1`,
  FileSystem `3.1.3`, Timers `3.1.3`, Routing `3.0.3`, and HTTP `3.0.3`.
  Composition adapter versions were not changed.

## Verification

- Focused suites passed: Nodes `37`, Engine `63`, Composition `54`,
  Composition.Hosting `17`, Fluent `21`, FileSystem `59`,
  FileSystem.Composition `27`, Timers `62`, Timers.Composition `14`, Routing
  `81`, Routing.Composition `17`, HTTP `17`, and HTTP.Composition `14`.
- Release tests passed `92`, including the unchanged public source-declaration
  baseline. Internal helper access was narrowed after the first release-test
  run exposed source-declaration scanner entries; no baseline was accepted or
  modified.
- Controlled Debug and Release solution builds passed with zero warnings and
  zero errors. The first combined build session stalled in Release; only the
  FluxFlow-owned build tree was stopped, `dotnet build-server shutdown` was
  run, and separate controlled builds then passed in about 27 seconds and 123
  seconds.
- Binary compatibility passed for all seven changed packages against their
  preceding published versions.
- Release preflight passed for all seven packages.
- A fresh temporary package source outside the repository was seeded with the
  seven current packages. Every fast release dry-run passed its package archive,
  isolated consumer smoke build, and feed verification checks against the temp
  source plus the public feed.

## Compatibility

No public API declarations changed, so `eng/public-api/baseline.txt` remains
unchanged. Runtime behavior changes are patch-level reliability corrections.
The package versions are prepared locally but are not tagged or published.
