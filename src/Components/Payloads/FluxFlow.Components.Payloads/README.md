# FluxFlow.Components.Payloads

Standalone exact-content inspection.

`PayloadInspectNode` accepts `FlowContent` and emits
`PayloadInspectionResult`. The result preserves the exact content, classifies
the payload, reports byte count/content type/encoding, and may include bounded
text or formatted previews and a detached parsed `JsonElement`.

Inspection does not add hidden decoded state to `FlowContent`. JSON/XML
formatting, base64 detection, and preview truncation are local inspection work.
Invalid or oversized input becomes `FlowError` on Output. Use Serialization
nodes when a decoded value must continue through the workflow.

## Composition

Install `FluxFlow.Components.Payloads.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
