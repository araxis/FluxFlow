# Projections Component Package

Date: 2026-06-03

## Package

- Package: `FluxFlow.Components.Projections`
- Version: `0.1.0-alpha.1`
- Tag: `components-projections-v0.1.0-alpha.1`

## Goal

Add a reusable event projection package that turns neutral runtime events into
in-memory projection snapshots for counters, latest-event views, rolling rates,
monitors, reports, and workflow checks.

## Decisions

- The package consumes engine-owned `FlowEvent` values through
  `event.projection`.
- Projection contracts are package-owned: `EventFilter`, `EventSummary`, and
  `EventProjectionSnapshot`.
- Filtering supports event type, type prefix, subject prefix, channel prefix,
  excluded subject/channel prefixes, status, source, source node id, component
  id through event attributes, attribute key/value pairs, and time ranges.
- Rate calculation uses event timestamps and a configurable rolling window.
- Snapshot timestamps use an injected `IProjectionClock` so tests and hosts can
  keep time deterministic.
- Final snapshots are evaluated at completion time, so the current-rate window
  reflects completion time rather than the last matched event.
- A null filter configuration is treated as match-all.
- The package has no UI framework, transport, storage, or host workspace
  dependency.

## Verification

- `dotnet build FluxFlow.sln -c Release --no-restore /nr:false`
- `dotnet test tests\FluxFlow.Components.Projections.Tests\FluxFlow.Components.Projections.Tests.csproj -c Release --no-restore /nr:false`
- `dotnet test tests\FluxFlow.Components.Timers.Tests\FluxFlow.Components.Timers.Tests.csproj -c Release --no-restore /nr:false`
- `dotnet test FluxFlow.sln -c Release --no-restore /nr:false`
- Direct clock scan confirmed only `SystemProjectionClock` reads current time
  directly in the package.
- Local package artifact was created in `artifacts\packages`.
- Mainline workflow passed after push.
- Release workflow passed for `components-projections-v0.1.0-alpha.1`.
- Public-feed restore smoke installed `FluxFlow.Components.Projections`
  `0.1.0-alpha.1` into a clean temporary console project and ran a neutral
  contract sample successfully.

## Review Notes

- A small timer schedule test stabilization was included because the full suite
  exposed a race in a pre-start disposal assertion. The test now awaits linked
  target completion instead of sampling completion state immediately after node
  completion.
- The post-feature review adjusted final snapshot semantics and null filter
  handling before release.

## Next

The next general-purpose package candidate is event expectations: a normal
workflow node package that waits for, matches, times out, and reports on neutral
events using the same `EventFilter` model.
