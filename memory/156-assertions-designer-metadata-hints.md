# Assertions Designer Metadata Hints

Date: 2026-06-29

## Summary

`FluxFlow.Components.Assertions.Composition` now follows the richer Designer
metadata hint pattern already used by Mapping and Control.

The change is descriptive metadata only. Runtime behavior, resource ownership,
renderer behavior, hot reload, and engine boundaries are unchanged.

## Changes

- Added option section, importance, editor, syntax, and related-resource hints
  to the assertion Designer metadata provider.
- Added host-owned resource key patterns for the `engine`, `contextFactory`,
  and `clock` resource hints:
  - `expression-engine:{name}`
  - `context-factory:{name}`
  - `clock:{name}`
- Bumped `FluxFlow.Components.Assertions.Composition` from `1.2.1` to `1.3.0`.
- Updated the package README and top-level changelog.
- Added focused assertions for option hints and resource picker hints.

## Verification

- `dotnet test tests\FluxFlow.Components.Assertions.Composition.Tests\FluxFlow.Components.Assertions.Composition.Tests.csproj --no-restore -v minimal`
  passed: 12 tests.
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  passed: 93 tests.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  passed: 84 tests.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed with 0 warnings and 0 errors.
- Local graph output was refreshed after the memory closeout: 11622 nodes,
  19610 edges, and 1076 communities. The local HTML graph was skipped because
  the graph exceeds the visualization size limit.

## Next

Continue the richer Designer metadata hint rollout one package family at a time.
Observability or State is the next reasonable candidate, but it should be
planned separately.
