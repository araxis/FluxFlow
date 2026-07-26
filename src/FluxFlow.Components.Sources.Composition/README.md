# FluxFlow.Components.Sources.Composition

Optional registrations and Designer metadata for generated and sequence
sources.

The generated configuration node emits `JsonElement`; the sequence node emits
`SequenceItem`. Metadata covers item/sequence, timing, emission, diagnostic,
type, and runtime options. The optional clock is host-owned. Both nodes expose
Output and Events only.

## DI Registration

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and exactly one SourcesComponentDesignMetadataProvider metadata provider through `IServiceCollection`:

```csharp
services.AddSourcesComponents();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.
