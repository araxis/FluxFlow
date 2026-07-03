# Timers Designer Metadata Hints

Date: 2026-06-30

## Summary

`FluxFlow.Components.Timers.Composition` now follows the richer Designer
metadata hint pattern used by Mapping, Control, Assertions, State,
Observability, Validation, and Routing.

The change is descriptive metadata only. Timer scheduling, source lifecycle,
transform behavior, clock use, time-zone handling, configuration binding,
runtime behavior, resource ownership, renderer behavior, hot reload, and engine
boundaries are unchanged.

## Changes

- Added option section, importance, and editor hints to the Interval, Schedule,
  Delay, Throttle, and Debounce Designer metadata.
- Omitted editor hints for duration and boolean options because the current
  Designer contract has no precise editor attribute value for either.
- Added a host-owned resource key pattern for the `clock` resource hint:
  - `clock:{name}`
- Preserved Schedule `omittedOptions=timeZone`, the omitted-options reason,
  ports, option kinds, defaults, required flags, min values, and runtime
  composition behavior.
- Bumped `FluxFlow.Components.Timers.Composition` from `1.4.2` to `1.5.0`.
- Updated the package README and top-level changelog.
- Added focused assertions for option hints, clock resource picker hints, and
  Schedule omitted time-zone metadata.

## Verification

- `dotnet test tests\FluxFlow.Components.Timers.Composition.Tests\FluxFlow.Components.Timers.Composition.Tests.csproj --no-restore -v minimal`
  passed: 14 tests.
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
