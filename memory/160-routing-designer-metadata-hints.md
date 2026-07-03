# Routing Designer Metadata Hints

Date: 2026-06-30

## Summary

`FluxFlow.Components.Routing.Composition` now follows the richer Designer
metadata hint pattern used by Mapping, Control, Assertions, State,
Observability, and Validation.

The change is descriptive metadata only. Dynamic port binding, selector
resolution, clock use, factory validation, runtime behavior, resource
ownership, renderer behavior, hot reload, and engine boundaries are unchanged.

## Changes

- Added option section, importance, editor, syntax, and related-resource hints
  to the Switch, Fork, Merge, Window, Correlation, and Join Designer metadata.
- Omitted editor hints for boolean options because the current Designer
  contract has no boolean editor attribute value.
- Added host-owned resource key patterns for routing selector delegate and
  clock resource hints:
  - `delegate:{name}`
  - `clock:{name}`
- Preserved dynamic output metadata attributes, required selector resources,
  ports, selector resolution, and runtime composition behavior.
- Bumped `FluxFlow.Components.Routing.Composition` from `1.2.3` to `1.3.0`.
- Updated the package README and top-level changelog.
- Added focused assertions for option hints, resource picker hints, dynamic
  output metadata, and required selector resources.

## Verification

- `dotnet test tests\FluxFlow.Components.Routing.Composition.Tests\FluxFlow.Components.Routing.Composition.Tests.csproj --no-restore -v minimal`
  passed: 17 tests.
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  passed: 93 tests.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  passed: 84 tests.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed with 0 warnings and 0 errors.
- Local graph output was refreshed after the memory closeout. The local HTML
  graph was skipped because the graph exceeds the visualization size limit.

## Next

Continue any further package-family metadata hint work as a separately planned
local pass.
