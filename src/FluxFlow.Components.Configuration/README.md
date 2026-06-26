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
  runtime validation, plus descriptor-only validation for declared references.
- `ConfigurationValidationRequestBuilder`: fluent helper that builds the same
  validation request DTOs used by configuration loading.

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

Fluent request construction is available when code wants the same DTO shape
without hand-assembling nested objects:

```csharp
var request = new ConfigurationValidationRequestBuilder()
    .AddResource("connections.primary", "primary", kind: "connection")
    .AddSecret("connections.primary.credential", "primary-credential")
    .AddOptionalResource("connections.secondary")
    .AddResources(componentResourceReferences)
    .AddSecrets(componentSecretReferences)
    .Build();

var report = await ConfigurationValidator.ValidateAsync(
    resourceLookup,
    secretResolver,
    request);
```

Design-time or configuration-only hosts can validate references against
descriptor providers without opening runtime resources or resolving secret
values:

```csharp
var report = ConfigurationValidator.ValidateDeclaredReferences(
    resourceDescriptorProvider,
    secretDescriptorProvider,
    request);
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
`ConfigurationValidationRequest` copies assigned resource and secret
collections, so later caller list mutations do not change what a constructed
request represents. Null collection assignments are preserved for structured
request diagnostics.
`ConfigurationValidationRequestBuilder` is only an authoring helper over these
same DTOs. It rejects null resource paths and secret option paths immediately,
while blank paths are still represented in the DTOs and reported later as
structured validation diagnostics. It does not own resource lookup, secret
resolution, or validation policy.
Descriptor-only validation uses host-owned resource and secret descriptor
providers. It checks declaration presence, kind mismatches, ambiguous secret
versions, and invalid descriptor provider output, but it does not open
resources or read secret values.
The builder can append individual references or existing reference collections
through `AddResources` and `AddSecrets`; built requests snapshot the current
builder contents.

Resource option paths trim surrounding whitespace when assigned, matching the
secret option path behavior from `FluxFlow.Components.Secrets`. Diagnostics and
metadata therefore report the normalized option path.

Configuration diagnostics trim textual fields when assigned. Diagnostic
metadata and validation report diagnostic collections are copied on assignment,
so later caller mutations do not change an already-created report.

Valid resource option metadata maps trim surrounding whitespace from keys and
values when assigned. Null maps, blank keys or values, and duplicate keys after
trimming are reported as structured configuration diagnostics.

## Composition

This package does not expose standalone nodes or `FluxFlow.Composition`
factories. Composition adapters may use these validation contracts indirectly
when a host wants to validate resource and secret references.
