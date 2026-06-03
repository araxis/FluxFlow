# 121 - Secrets Option Resolution Helpers

## Status

Published `FluxFlow.Components.Secrets` `0.2.0-alpha.1`.

## Decision

Components should expose `SecretReference` in option models, not resolved secret
values. Hosts continue to own resolution by providing an `ISecretResolver`.

The package now includes a small option helper layer so components can resolve
required or optional references and receive an option-level result with
diagnostics.

## Scope

- Added `SecretOptionReference`.
- Added `SecretOptionResolution`.
- Added `SecretOptionResolver` with required, optional, and ordered batch
  resolution helpers.
- Added `MissingSecretReference` diagnostic code.
- Added option validation with option path metadata and inner reference field
  path metadata.
- Updated README examples to show option models holding `SecretReference`.
- Added focused tests for required resolution, missing required reference,
  optional missing reference, ordered batch results, invalid option validation,
  and option path diagnostics.

## Verification

- `dotnet test tests\FluxFlow.Components.Secrets.Tests\FluxFlow.Components.Secrets.Tests.csproj --configuration Release`
- `dotnet build FluxFlow.sln --configuration Release`
- `dotnet test FluxFlow.sln --configuration Release --no-build`
- `dotnet pack src\FluxFlow.Components.Secrets\FluxFlow.Components.Secrets.csproj --configuration Release --no-build --output artifacts\packages`
- Release workflow run `26905067539`.
- Public-feed smoke returned `credential:[redacted]`.

## Next

Continue the component maturity backlog. Good next candidates:

- Add guidance or helpers for component packages that need multiple secret
  references in one options object.
- Revisit resources and secrets together if hosts need a unified validation
  report.
- Continue package hardening only when a concrete host integration needs it.
