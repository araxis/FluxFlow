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
- `SecretOptionReference`: an option path plus optional secret reference.
- `SecretOptionResolver`: helper for resolving required or optional secret
  option references through a host-provided resolver.
- `SecretOptionResolution`: option-level resolved value, missing state, or
  structured diagnostic.
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

## Component Options

Component option models should store references, not resolved values:

```csharp
using FluxFlow.Components.Secrets;
using FluxFlow.Components.Secrets.Contracts;

public sealed record SenderOptions
{
    public SecretReference? Credential { get; init; }
}

var optionResult = await SecretOptionResolver.ResolveRequiredAsync(
    hostResolver,
    options.Credential,
    "credential",
    cancellationToken);

if (!optionResult.Resolved)
{
    Console.WriteLine(optionResult.Diagnostic);
    return;
}

var credential = optionResult.Value.Reveal();
```

The component owns its option shape and error handling. The host owns the
resolver implementation and decides where the value comes from.

## Diagnostics

Use `SecretDiagnostics` to:

- validate secret records and references
- validate option references
- find duplicate declarations
- find references that cannot be resolved

Metadata and attribute maps are validated as part of records, references, and
option references; null maps are reported as structured invalid-secret
diagnostics.

`SecretName`, secret `Version`, `Kind`, `DisplayName`, `Summary`, and secret
option paths trim surrounding whitespace when assigned. This keeps
configuration-bound records and references matching the same logical name,
version, and kind, and makes duplicate detection catch declarations that differ
only by padding.

Valid metadata, attribute, and option metadata maps trim surrounding whitespace
from keys and values when assigned. Maps with null values, blank keys or values,
or duplicate keys after trimming are preserved so `SecretDiagnostics` can report
structured invalid-secret diagnostics.

Secret declarations are unique by name plus optional version. When multiple
versions exist, callers should provide `Version` or another narrowing field
such as `Kind`.

## Boundaries

This package does not own concrete secret storage. It only defines neutral
contracts and helper logic. Hosts own persistence, access control, refresh,
rotation, auditing, and disposal.

## Composition

This package does not expose standalone nodes or `FluxFlow.Composition`
factories. Component options should keep secret references; hosts and adapters
resolve them through a host-owned `ISecretResolver` before constructing
resources that need secret values.
