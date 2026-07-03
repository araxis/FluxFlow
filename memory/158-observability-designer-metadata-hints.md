# Observability Designer Metadata Hints

Date: 2026-06-30

## Summary

`FluxFlow.Components.Observability.Composition` now follows the richer Designer
metadata hint pattern used by Mapping, Control, Assertions, and State.

The change is descriptive metadata only. Runtime behavior, resource ownership,
renderer behavior, hot reload, and engine boundaries are unchanged.

## Changes

- Added option section, importance, editor, syntax, and related-resource hints
  to the observability Counter, Logger, and Metrics Designer metadata.
- Omitted editor hints where the current Designer contract has no precise enum
  or multiline editor value.
- Added host-owned resource key patterns for expression engine, context
  factory, selector, and clock resource hints:
  - `expression-engine:{name}`
  - `context-factory:{name}`
  - `selector:{name}`
  - `attribute:{name}`
  - `clock:{name}`
- Preserved the Counter `engine` conditional requirement for `predicate` or
  `expression`, and the Logger `attribute:{name}` option link for
  `attributeSelectors`.
- Bumped `FluxFlow.Components.Observability.Composition` from `1.2.2` to
  `1.3.0`.
- Updated the package README and top-level changelog.
- Added focused assertions for option hints and resource picker hints.

## Verification

- `dotnet test tests\FluxFlow.Components.Observability.Composition.Tests\FluxFlow.Components.Observability.Composition.Tests.csproj --no-restore -v minimal`
  passed: 22 tests.
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  passed: 93 tests.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  passed: 84 tests.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed with 0 warnings and 0 errors after shutting down stale build servers
  from the timed-out first attempt.
- Local graph output was refreshed after the memory closeout. The local HTML
  graph was skipped because the graph exceeds the visualization size limit.

## Next

Continue the richer Designer metadata hint rollout one package family at a
time. Validation or Routing is a reasonable later candidate, but the next pass
should be planned separately.
