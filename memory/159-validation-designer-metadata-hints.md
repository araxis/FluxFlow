# Validation Designer Metadata Hints

Date: 2026-06-30

## Summary

`FluxFlow.Components.Validation.Composition` now follows the richer Designer
metadata hint pattern used by Mapping, Control, Assertions, State, and
Observability.

The change is descriptive metadata only. Runtime behavior, resource ownership,
renderer behavior, hot reload, and engine boundaries are unchanged.

## Changes

- Added option section, importance, editor, and related-resource hints to the
  JSON schema validator Designer metadata.
- Added host-owned resource key patterns for the `selector` and `clock`
  resource hints:
  - `selector:{name}`
  - `clock:{name}`
- Preserved JSON schema loading, selector fallback behavior, `payloadSelector`
  compatibility, ports, and runtime composition behavior.
- Bumped `FluxFlow.Components.Validation.Composition` from `1.2.2` to `1.3.0`.
- Updated the package README and top-level changelog.
- Added focused assertions for option hints and resource picker hints.

## Verification

- `dotnet test tests\FluxFlow.Components.Validation.Composition.Tests\FluxFlow.Components.Validation.Composition.Tests.csproj --no-restore -v minimal`
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

Continue the richer Designer metadata hint rollout one package family at a
time. Routing is a reasonable later candidate, but the next pass should be
planned separately.
