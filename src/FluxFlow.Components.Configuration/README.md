# FluxFlow.Components.Configuration

Reusable configuration validation report helpers for FluxFlow.

## Purpose

This package lets hosts validate resource references and secret option
references into one neutral report. It does not own resources, secret storage,
application files, or runtime lifecycle.

## Contracts

- `ConfigurationResourceReference`: a resource option path plus optional
  `ResourceReference`.
- `ConfigurationValidationRequest`: resource references and secret option
  references to validate.
- `ConfigurationDiagnostic`: normalized validation issue with source, code,
  severity, path, name, kind, and metadata.
- `ConfigurationValidationReport`: ordered diagnostics plus summary counts.
- `ConfigurationValidator`: helper for resource-only, secret-only, or combined
  validation.

## Example

```csharp
using FluxFlow.Components.Configuration;
using FluxFlow.Components.Configuration.Contracts;
using FluxFlow.Components.Resources.Contracts;
using FluxFlow.Components.Secrets.Contracts;

var report = await ConfigurationValidator.ValidateAsync(
    resourceLookup,
    secretResolver,
    new ConfigurationValidationRequest
    {
        Resources =
        [
            new ConfigurationResourceReference
            {
                Path = "connections.primary",
                Reference = new ResourceReference
                {
                    Name = new ResourceName("primary"),
                    Kind = "connection"
                }
            }
        ],
        Secrets =
        [
            new SecretOptionReference
            {
                OptionPath = "connections.primary.credential",
                Reference = new SecretReference
                {
                    Name = new SecretName("primary-credential")
                }
            }
        ]
    });

Console.WriteLine(report.HasErrors);
```

## Boundaries

This package only normalizes validation. Hosts still decide where resources and
secret values live, how they are secured, when they are resolved, and how
diagnostics are displayed or logged.

Resource option metadata is validated as configuration input. Null maps, empty
keys, and empty values are reported as structured configuration diagnostics.
Null request-level resource/secret collections and null validation entries are
also reported as structured configuration diagnostics, which keeps
configuration-file binding issues in the validation report instead of surfacing
as normalization exceptions.

Resource option paths trim surrounding whitespace when assigned, matching the
secret option path behavior from `FluxFlow.Components.Secrets`. Diagnostics and
metadata therefore report the normalized option path.

## Composition

This package does not expose standalone nodes or `FluxFlow.Composition`
factories. Composition adapters may use these validation contracts indirectly
when a host wants to validate resource and secret references.
