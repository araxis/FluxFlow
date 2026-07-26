# FluxFlow.Components.Payloads.Composition

Optional registration and Designer metadata for `payload.inspect`.

The descriptor declares `FlowContent` Input,
`PayloadInspectionResult` Output, and Events. Limit, preview, detection,
formatting, and runtime options remain flat. The optional clock is host-owned.

There is no codec-catalog resource, cached decoded value, result wrapper, or
Errors port. Parsing needed by downstream nodes is an explicit Serialization
step.

## Registration And Design Metadata

Register components with `RegisterPayloadInspect`. `PayloadsComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
