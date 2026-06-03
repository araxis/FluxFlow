# Configuration Validation Package

## 2026-06-03

Created `FluxFlow.Components.Configuration` `0.1.0-alpha.1`.

## Decision

Keep Resources and Secrets independent, then add a small opt-in join layer for
hosts that want one validation report across named resources and secret-backed
options.

This avoids forcing resource lookup concerns into the secrets package, and avoids
forcing secret resolution concerns into the resources package. Hosts can use the
combined validator only where it is useful for startup checks, editor feedback,
or deployment validation.

## Scope

- `ConfigurationValidationRequest` groups resource references and secret option
  references.
- `ConfigurationValidationReport` exposes ordered diagnostics plus error and
  warning counts.
- `ConfigurationDiagnostic` preserves source, code, severity, message, option
  path, reference name, kind, and metadata.
- `ConfigurationValidator` validates resources and secrets through host-provided
  `IResourceLookup` and `ISecretResolver` implementations.
- Optional missing references are allowed.
- Required missing resource references produce a configuration diagnostic.
- Secret option validation preserves all option diagnostics before resolution.

## Verification

- Focused package tests passed.
- Full solution build passed.
- Full solution tests passed.
- Package pack passed.
- Release workflow run `26906956190` passed.
- Public-feed smoke consumed `FluxFlow.Components.Configuration`
  `0.1.0-alpha.1` and returned `False:0`.

## Next

Continue the broader component maturity backlog. Use the configuration package
only when a host needs a combined resource and secret validation report.
