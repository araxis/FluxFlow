# FluxFlow.Components.Secrets

Secret references, non-sensitive descriptors, resolution helpers, and redaction
contracts for FluxFlow hosts.

## Purpose

Secrets are addressed through the same canonical nested resource address space
as other host resources. A secret identity therefore looks like
`Resources.Credentials.CommandClientPassword`, not a flat provider-specific
name.

The package does not own a secret store. Hosts decide persistence, access,
refresh, rotation, auditing, and lifetime.

## Contracts

- `SecretName`: a resource-only wrapper over a canonical
  `ApplicationAddress`.
- `SecretReference`: an address plus optional version, kind, and attributes.
- `SecretDescriptor`: non-sensitive metadata with explicit
  `ResourceOwnership`.
- `SecretValue`: a resolved value whose string formatting is always redacted.
- `ISecretResolver`: runtime resolution abstraction.
- `ISecretDescriptorProvider`: optional non-sensitive descriptor enumeration.
- `SecretOptionReference` and `SecretOptionResolver`: required/optional option
  resolution helpers.
- `InMemorySecretResolverBuilder`: a local/test authoring helper.
- `SecretDiagnostics` and `SecretRedactor`: structured validation and
  redaction helpers.

Secret descriptor ownership uses the shared values from
`FluxFlow.Components.Resources`: `Host`, `ResourceRevision`, or `External`.

## Example

```csharp
using FluxFlow.Components.Resources.Contracts;
using FluxFlow.Components.Secrets;
using FluxFlow.Components.Secrets.Contracts;
using FluxFlow.Composition.Addressing;

var passwordAddress = ApplicationAddress.Resource(
    "Credentials",
    "CommandClientPassword");

var resolver = new InMemorySecretResolverBuilder()
    .Add(
        passwordAddress,
        "value-from-host",
        ResourceOwnership.Host,
        kind: "credential")
    .BuildResolver();

var result = await resolver.ResolveAsync(new SecretReference
{
    Name = new SecretName(passwordAddress),
    Kind = "credential"
});

Console.WriteLine(result.Resolved);
Console.WriteLine(result.Value); // redacted
```

Component option models retain references, not resolved values. A host or
resource factory resolves the reference before constructing the client that
needs it.

## Keyed Registration

Factory registration transfers creation/disposal to the provider:

```csharp
var resolverAddress = ApplicationAddress.Resource("Infrastructure", "Secrets");
services.AddFluxFlowSecretResolver(resolverAddress, provider => BuildResolver(provider));
```

External registration remains non-owning:

```csharp
services.AddExternalFluxFlowSecretResolver(resolverAddress, existingResolver);
```

Descriptor providers use the corresponding provider-owned and external helper
names. Resolver registration does not automatically expose descriptor metadata
because descriptor enumeration is optional and may be security-sensitive.

## Diagnostics And Redaction

Secret records require a canonical resource address, explicit ownership, and a
value. References preserve optional version and kind matching. Diagnostic
formatting and `SecretValue.ToString()` never reveal resolved values.

## Boundaries

This package does not create application resources, parse application JSON,
choose deployment ownership, build provider snapshots, or participate in
workflow routing. Composition owns addresses; Hosting owns provider snapshots;
the host owns secret policy.

## Composition

This package references `FluxFlow.Composition` only for canonical resource
addresses. It does not expose application component descriptors or participate in
workflow execution.
