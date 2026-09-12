# FluxFlow.Components.Serialization

Standalone explicit content conversion nodes.

All six nodes implement `IFlowComponent` and work directly with
`workflow.Add("parse", new JsonParseNode())`. `Ports` reuses the existing canonical
Input/Output/Events blocks; no creator or separate port registration is required.
Upstream errors pass through as the original message without conversion or a new
causation edge. Successful conversions still produce derived result messages.

| Node | Input | Output value |
|------|-------|--------------|
| `JsonParseNode` | `FlowContent` | detached `JsonElement` |
| `JsonStringifyNode` | `JsonElement` | `FlowContent` |
| `TextDecodeNode` | `FlowContent` | string |
| `TextEncodeNode` | string | `FlowContent` |
| `Base64EncodeNode` | `FlowContent` | string |
| `Base64DecodeNode` | string | `FlowContent` |

Size limits, encoding fallback, JSON options, exact bytes, content type, and
truncation behavior remain explicit. Conversion failure becomes `FlowError` on
the same Output. There are no lazy codecs or alternate hidden representations.

Decode once before fan-out when several consumers need the same decoded value;
branch before conversion when exact raw bytes must also continue.

## Composition

Install `FluxFlow.Components.Serialization.Composition` for optional runtime registration and authoring. Editor metadata is separate: install `FluxFlow.Components.Serialization.Designer` and call `AddSerializationDesign()` after runtime registration. This runtime package remains free of Composition, Designer, and Engine dependencies.
