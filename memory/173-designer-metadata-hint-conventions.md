# Designer Metadata Hint Conventions

Date: 2026-06-30

## Summary

Closed the component-family Designer metadata hint rollout with release-test
convention coverage. The change adds generic guardrails for option hints and
host-owned resource key patterns across composition metadata providers.

## Changes

- Extended `ComponentCompositionMetadataConventionTests` so editable Designer
  options must declare non-empty `section` and `importance` attributes.
- Added contract-value validation for option `importance`, `editor`, and
  `syntax` attributes using the current Designer attribute values.
- Added same-node validation for option `relatedResource` attributes.
- Tightened host-owned resource metadata checks so every host-owned resource
  declares a non-empty key pattern containing `{name}`.
- Kept resource key-pattern validation generic: patterns must align with the
  picker kind or with a named resource pattern such as `attribute:{name}`.
- Left runtime behavior, provider metadata content, package versions, public
  APIs, package READMEs, package release notes, and the top-level changelog
  unchanged.

## Verification

- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - Passed: 85
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  - Passed: 93
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Passed with 0 warnings and 0 errors.
- `graphify update . --force`
  - Refreshed local graph output after the memory edits.
  - `graphify-out/` remains excluded from git.

## Next

Keep further convention or package-family Designer metadata work separately
planned, locally scoped, and locally committed.
