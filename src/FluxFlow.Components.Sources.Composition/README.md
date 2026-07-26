# FluxFlow.Components.Sources.Composition

Optional registrations and Designer metadata for generated and sequence
sources.

The generated configuration node emits `JsonElement`; the sequence node emits
`SequenceItem`. Metadata covers item/sequence, timing, emission, diagnostic,
type, and runtime options. The optional clock is host-owned. Both nodes expose
Output and Events only.

## Registration And Design Metadata

Register components with `RegisterGeneratedSource`, `RegisterSequenceSource`. `SourcesComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
