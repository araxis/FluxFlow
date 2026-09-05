# FluxFlow.Components.Sources.Composition

Optional registrations and Designer metadata for generated and sequence
sources.

The generated configuration node emits `JsonElement`; the sequence node emits
`SequenceItem`. Metadata covers item/sequence, timing, emission, diagnostic,
type, and runtime options. The optional clock is host-owned. Both nodes expose
Output and Events only.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit SourcesComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddSources();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`SourcesComponents` exposes `GeneratedSource` and `SequenceSource` typed contracts. The retained `AddX` methods use those same contracts, and both handles expose `Output` and `Events`. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from these contracts retains its executable descriptors.
Normal code-first hosting therefore calls only `AddFluxFlow(definition)` and
does not repeat the family registration above. Use that service registration
for JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contracts.
