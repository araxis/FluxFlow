# FluxFlow.Components.Serialization.Composition

Optional registrations and Designer metadata for JSON, text, and Base64
conversion nodes.

Descriptors expose the exact `FlowContent`, `JsonElement`, or string port types
listed by the runtime package, one Output, and Events. Encoding, JSON, size, and
runtime options remain flat. The optional clock is host-owned.

Errors share Output. No codec catalog, result wrapper, or universal Errors port
is registered.

## Registration And Design Metadata

Register components with `RegisterBase64Decode`, `RegisterBase64Encode`, `RegisterJsonParse`, `RegisterJsonStringify`, `RegisterTextDecode`, `RegisterTextEncode`. `SerializationComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
