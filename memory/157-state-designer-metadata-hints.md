# State Designer Metadata Hints

Date: 2026-06-29

## Summary

`FluxFlow.Components.State.Composition` now follows the richer Designer metadata
hint pattern used by Mapping, Control, and Assertions.

The change is descriptive metadata only. Runtime behavior, resource ownership,
renderer behavior, hot reload, and engine boundaries are unchanged.

## Changes

- Added option section, importance, editor, syntax, and related-resource hints
  to the state reducer Designer metadata provider.
- Added host-owned resource key patterns for the `engine` and `clock` resource
  hints:
  - `expression-engine:{name}`
  - `clock:{name}`
- Bumped `FluxFlow.Components.State.Composition` from `1.2.1` to `1.3.0`.
- Updated the package README and top-level changelog.
- Added focused assertions for option hints and resource picker hints.

## Verification

- `dotnet test tests\FluxFlow.Components.State.Composition.Tests\FluxFlow.Components.State.Composition.Tests.csproj --no-restore -v minimal`
  passed: 15 tests.
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  passed: 93 tests.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  passed: 84 tests.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed with 0 warnings and 0 errors.
- Local graph output was refreshed after the memory closeout. The local HTML
  graph was skipped because the graph exceeds the visualization size limit.

## Next

Continue the richer Designer metadata hint rollout one package family at a time.
Observability is the next reasonable candidate because it already has
expression-related counter options and host-owned selector, context factory, and
clock resources.
