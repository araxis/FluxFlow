# FluxFlow.Components.Secrets

Reusable secret reference and resolution contracts for FluxFlow.

## Purpose

This package lets component packages refer to secret values by name without
coupling to a concrete secret store. Hosts decide where values live, how access
is controlled, how values are refreshed, and how ownership is handled.

## Contracts

- `SecretReference`: a name plus optional version, kind, and attributes.
- `SecretDescriptor`: non-sensitive metadata for a declared secret.
- `SecretValue`: resolved value wrapper with redacted string formatting.
- `ISecretResolver`: runtime abstraction for resolving a reference.
- `SecretResolveResult`: resolved value or structured diagnostic.
- `SecretDiagnostic`: stable diagnostics for missing, duplicate, ambiguous,
  kind mismatch, denied, failed, and invalid secret references.
- `SecretRedactor`: helper for redacting text and sensitive attribute values.

## Example

```csharp
using FluxFlow.Components.Secrets;
using FluxFlow.Components.Secrets.Contracts;

var resolver = new InMemorySecretResolver(
[
    new SecretRecord
    {
        Descriptor = new SecretDescriptor
        {
            Name = new SecretName("primary-token"),
            Kind = "profile"
        },
        Value = new SecretValue("value-from-host")
    }
]);

var result = await resolver.ResolveAsync(new SecretReference
{
    Name = new SecretName("primary-token"),
    Kind = "profile"
});

Console.WriteLine(result.Resolved);
Console.WriteLine(result.Value);
```

## Diagnostics

Use `SecretDiagnostics` to:

- validate secret records and references
- find duplicate declarations
- find references that cannot be resolved

Secret declarations are unique by name plus optional version. When multiple
versions exist, callers should provide `Version` or another narrowing field
such as `Kind`.

## Boundaries

This package does not own concrete secret storage. It only defines neutral
contracts and helper logic. Hosts own persistence, access control, refresh,
rotation, auditing, and disposal.
