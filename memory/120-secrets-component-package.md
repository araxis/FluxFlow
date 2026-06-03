# 120 - Secrets Component Package

## Status

Published `FluxFlow.Components.Secrets` `0.1.0-alpha.1`.

## Decision

Secrets are a neutral contract package, not a runtime node package. Component
packages can reference secret values by name, while hosts keep ownership of
where values live, access checks, refresh, rotation, auditing, and disposal.

The package includes an in-memory resolver only as a lightweight composition
and test helper. It is not a concrete external secret store adapter.

## Scope

- Added `SecretReference` with name, optional version, optional kind, and
  attributes.
- Added `SecretDescriptor` for non-sensitive declaration metadata.
- Added `SecretValue` with safe string formatting.
- Added `ISecretResolver` and `SecretResolveResult`.
- Added structured diagnostics for invalid, missing, duplicate, ambiguous, kind
  mismatch, denied, and failed resolution cases.
- Added redaction helpers for values and sensitive attributes.
- Added `InMemorySecretResolver` with name/version uniqueness and kind-aware
  narrowing.
- Added focused tests for success, missing, kind mismatch, ambiguity, version
  selection, duplicate detection, invalid references, redaction, safe
  formatting, and unresolved reference diagnostics.

## Verification

- `dotnet test tests\FluxFlow.Components.Secrets.Tests\FluxFlow.Components.Secrets.Tests.csproj --configuration Release`
- `dotnet build FluxFlow.sln --configuration Release`
- `dotnet test FluxFlow.sln --configuration Release --no-build`
- `dotnet pack src\FluxFlow.Components.Secrets\FluxFlow.Components.Secrets.csproj --configuration Release --no-build --output artifacts\packages`
- Release workflow run `26903715647`.
- Public-feed smoke returned `True:[redacted]`.

## Next

Continue the broader component maturity backlog. Good next candidates:

- Revisit storage only when a concrete host needs another adapter.
- Add a small host integration helper that shows how existing components should
  accept `SecretReference` in options without owning resolution.
- Continue package hardening around resource and secret diagnostics if hosts
  need richer validation reports.
