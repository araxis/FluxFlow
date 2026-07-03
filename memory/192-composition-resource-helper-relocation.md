# Composition Resource Helper Relocation

Date: 2026-07-03

## Summary

Completed a dependency-hygiene pass so composition adapters no longer need a
`FluxFlow.Composition.Hosting` reference. Keyed resource resolution now lives
as `CompositionNodeFactoryContext` instance methods in `FluxFlow.Composition`,
and the node kit gained an optional clock for deterministic safety-net error
timestamps.

## Changes

- Added `GetRequiredResourceKey`, `GetRequiredResource<TResource>`, and
  `GetResource<TResource>` as `CompositionNodeFactoryContext` instance methods
  in `FluxFlow.Composition`, with a
  `Microsoft.Extensions.DependencyInjection.Abstractions` package reference for
  keyed service resolution.
- Marked `CompositionNodeFactoryContextResourceExtensions` in
  `FluxFlow.Composition.Hosting` obsolete; the extensions now delegate to the
  context instance methods, and instance methods win overload resolution so
  existing callers keep identical behavior without ambiguity.
- Removed the `FluxFlow.Composition.Hosting` project reference and namespace
  using from all 19 `.Composition` adapter packages. Adapter registration code
  is unchanged; the same `context.GetResource<...>(...)` calls now bind to the
  instance methods.
- Added `FlowNodeOptions.Clock` (`TimeProvider`, defaults to
  `TimeProvider.System`); `FlowNode` stamps its safety-net error (a
  `ProcessAsync` throw) with the configured clock instead of
  `DateTimeOffset.UtcNow`.
- Bumped `FluxFlow.Nodes` to `1.2.0`, `FluxFlow.Composition` to `1.1.0`, and
  `FluxFlow.Composition.Hosting` to `1.1.0` with changelog entries and package
  release notes, then accepted the public API baseline through the documented
  release-test acceptance flow.
- Updated the `FluxFlow.Composition` and `FluxFlow.Composition.Hosting`
  READMEs and `docs/05-hosting-and-observability.md` so resource resolution
  ownership reads correctly.
- Fast-forwarded local `main` to the published Designer host layer planning
  state (`88027c7`); pushing `origin/main` remains an operator step.

## Boundaries

- Adapter package versions were not bumped; their published packages still
  declare a `FluxFlow.Composition.Hosting` dependency until each adapter's
  next release. Bump adapter versions at the next release prep.
- No node runtime behavior changed apart from the error timestamp source.
- No tags were created and nothing was published.

## Verification

- `dotnet build FluxFlow.sln --configuration Release` passed with 0 warnings
  and 0 errors.
- Release convention tests passed: `92` passed, `0` failed, `0` skipped.
- Full no-build Release solution suite passed: `1707` passed, `0` failed,
  `0` skipped across 59 test assemblies.
- Public API baseline re-accepted; only the intended `FluxFlow.Nodes` and
  `FluxFlow.Composition` entries changed.
