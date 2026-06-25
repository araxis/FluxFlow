# FluxFlow.Components.Resources

Reusable named resource contracts for FluxFlow.

## Purpose

This package lets hosts describe and resolve named resources without coupling
component packages to a concrete owner or lifecycle model.

## Contracts

- `ResourceReference`: a name plus optional kind and attributes.
- `ResourceDescriptor`: a declared resource name, optional kind, display fields,
  and metadata.
- `IResourceLookup`: a small lookup abstraction hosts can back with their own
  resource lifecycle.
- `ResourceLookupResult`: lookup outcome plus a structured diagnostic when a
  resource cannot be used.
- `ResourceDiagnostic`: stable diagnostics for missing, duplicate, unused, kind
  mismatch, and invalid resources.

## Example

```csharp
using FluxFlow.Components.Resources;
using FluxFlow.Components.Resources.Contracts;

var catalog = new ResourceDescriptorCatalog(
[
    new ResourceDescriptor
    {
        Name = new ResourceName("primary-profile"),
        Kind = "profile",
        DisplayName = "Primary Profile",
        Metadata = new Dictionary<string, string>
        {
            ["owner"] = "runtime"
        }
    }
]);

var result = await catalog.LookupAsync(new ResourceReference
{
    Name = new ResourceName("primary-profile"),
    Kind = "profile"
});

Console.WriteLine(result.Found);
```

## Diagnostics

Use `ResourceDiagnostics` to:

- validate descriptors and references
- find duplicate descriptors
- find missing references
- find unused descriptors

Metadata and attribute maps are validated as part of descriptors and references;
null maps are reported as structured invalid-resource diagnostics.
Null descriptor entries and null reference entries inside helper collections are
reported or ignored by the relevant diagnostic helpers instead of surfacing
accidental null-reference failures.

`ResourceName`, resource `Kind`, `DisplayName`, and `Summary` trim surrounding
whitespace when assigned. This keeps configuration-bound descriptors and
references matching the same logical name and kind, and makes duplicate
detection catch names that differ only by padding.

Valid metadata and attribute maps trim surrounding whitespace from keys and
values when assigned. Maps with null values, blank keys or values, or duplicate
keys after trimming are preserved so `ResourceDiagnostics` can report structured
invalid-resource diagnostics.

## Boundaries

This package only defines resource contracts and helper logic. Hosts decide how
resources are created, secured, refreshed, shared, disposed, and displayed.

## Composition

This package does not expose standalone nodes or `FluxFlow.Composition`
factories. Composition adapters consume host-owned resources through their own
keyed resource names and may use these contracts for higher-level resource
catalogs.
