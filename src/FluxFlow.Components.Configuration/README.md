# FluxFlow.Components.Configuration

Combined resource-reference and secret-reference validation for FluxFlow hosts.

## Purpose

This package turns resource and secret validation into one neutral report. It
uses canonical nested `ApplicationAddress` resource identities and validates
the explicit ownership metadata declared by resource and secret descriptor
providers.

It does not load the application document, own resources, resolve deployment
policy, or manage runtime revisions.

## Contracts

- `ConfigurationResourceReference`: a component option path plus an optional
  `ResourceReference`.
- `ConfigurationOptionPath`: a non-empty code-authored option path.
- `ConfigurationValidationRequest`: resource and secret references to check.
- `ConfigurationDiagnostic`: normalized source, code, severity, path, address,
  kind, and metadata.
- `ConfigurationValidationReport`: ordered diagnostics and summary counts.
- `ConfigurationValidator`: runtime lookup/resolution validation and
  descriptor-only validation.
- `ConfigurationValidationRequestBuilder`: fluent request construction using
  canonical addresses.

## Example

```csharp
using FluxFlow.Components.Configuration;
using FluxFlow.Composition.Addressing;

var clientAddress = ApplicationAddress.Resource("Messaging", "Client1");
var credentialAddress = ApplicationAddress.Resource(
    "Credentials",
    "Client1Password");

var request = new ConfigurationValidationRequestBuilder()
    .AddResource(
        "client",
        clientAddress,
        kind: "mqtt.client")
    .AddSecret(
        "password",
        credentialAddress,
        kind: "credential")
    .AddOptionalResource("retryPolicy")
    .Build();

var report = await ConfigurationValidator.ValidateAsync(
    resourceLookup,
    secretResolver,
    request);
```

Typed `ResourceName` and `SecretName` overloads remain available when a caller
already has a reference DTO. Code starting from the application model should
pass `ApplicationAddress` directly.

## Declared Reference Validation

Design-time and activation-time callers can validate without opening resources
or reading secret values:

```csharp
var report = ConfigurationValidator.ValidateDeclaredReferences(
    resourceDescriptorProvider,
    secretDescriptorProvider,
    request);
```

Descriptor validation checks canonical addresses, required ownership, duplicate
declarations, kind mismatches, and ambiguous secret versions. Runtime
validation additionally calls the host-owned lookup and resolver abstractions.

## Boundaries

The validator reports configuration facts; it does not select `Host`,
`ResourceRevision`, or `External` ownership, create resources, transfer
disposal ownership, build service providers, or apply revisions. Composition
owns the address model and Hosting owns immutable provider snapshots and
transactional activation.

## Composition

This package references `FluxFlow.Composition` only for canonical application
resource addresses. It does not expose application component descriptors or load the
application document.
