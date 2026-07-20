# FluxFlow.Components.Resources

Canonical resource references, descriptor catalogs, diagnostics, and keyed
registration helpers for FluxFlow hosts.

## Purpose

This package describes resources without creating protocol clients, stores, or
other concrete infrastructure. Resource identity uses the same ordinal,
case-sensitive `ApplicationAddress` model as application definitions, runtime
ports, and Hosting snapshots.

Every resource name is a canonical nested address beginning with `Resources`,
for example `Resources.Messaging.Client1`. Flat names and workflow/component
addresses are rejected.

## Contracts

- `ResourceName`: a resource-only wrapper over a canonical
  `ApplicationAddress`.
- `ResourceReference`: a resource name plus optional kind and attributes.
- `ResourceDescriptor`: a declared resource name, explicit ownership, optional
  kind, display fields, and metadata.
- `ResourceOwnership`: `Host`, `ResourceRevision`, or `External`.
- `IResourceDescriptorProvider`: enumerates declared resource metadata.
- `IResourceLookup`: resolves references and exposes declared metadata.
- `ResourceDescriptorCatalog`: validates and resolves descriptor snapshots.
- `ResourceDiagnostics`: validates declarations and references and reports
  missing, duplicate, unused, kind-mismatched, and invalid resources.

`ResourceOwnership` records the lifetime decision made by the host:

- `Host`: the host-lifetime provider owns creation and disposal.
- `ResourceRevision`: a resource-revision provider owns creation and disposal.
- `External`: an instance is bridged without transferring disposal ownership.

Ownership is descriptor metadata. This package does not build provider
snapshots or decide which ownership value a deployment must use.

## Catalog Example

```csharp
using FluxFlow.Components.Resources;
using FluxFlow.Components.Resources.Contracts;
using FluxFlow.Composition.Addressing;

var clientAddress = ApplicationAddress.Resource("Messaging", "Client1");
var catalog = new ResourceDescriptorCatalogBuilder()
    .Add(
        clientAddress,
        ResourceOwnership.ResourceRevision,
        kind: "mqtt.client",
        displayName: "Command Client")
    .BuildCatalog();

var result = await catalog.LookupAsync(new ResourceReference
{
    Name = new ResourceName(clientAddress),
    Kind = "mqtt.client"
});

Console.WriteLine(result.Found);
```

`ResourceName.Address` returns the parsed canonical address. The constructor
also accepts a canonical address string when a configuration adapter already
has one.

## Keyed Registration

Factory registration is provider-owned:

```csharp
var catalogAddress = ApplicationAddress.Resource("Infrastructure", "Catalog");

services.AddFluxFlowResourceLookup(
    catalogAddress,
    provider => BuildCatalog(provider));
```

External registration is explicitly non-owning:

```csharp
services.AddExternalFluxFlowResourceLookup(catalogAddress, existingCatalog);
```

Both lookup registrations expose `IResourceLookup` and a non-owning
`IResourceDescriptorProvider` view under `catalogAddress.Value`. The view keeps
one provider-created lookup from being disposal-tracked twice and does not
transfer ownership of an external lookup.

Descriptor-only providers have matching
`AddFluxFlowResourceDescriptorProvider(...)` and
`AddExternalFluxFlowResourceDescriptorProvider(...)` helpers.

## Diagnostics

Descriptor validation requires a canonical name, a defined ownership value,
valid optional text, and valid metadata. Reference validation requires a
canonical name and valid optional kind/attributes. Maps are copied and
normalized when valid; malformed maps remain diagnosable rather than failing
during configuration binding.

## Boundaries

This package does not create or dispose concrete application resources, parse
the application document, build provider snapshots, perform workflow routing,
or expose standalone nodes. Composition owns definitions and addressing;
Hosting owns provider boundaries and revisions; concrete resource packages own
their runtime clients and adapters.

## Composition

This package references `FluxFlow.Composition` only for the canonical
`ApplicationAddress` contract. It does not expose composition node factories,
load application definitions, or perform routing.
