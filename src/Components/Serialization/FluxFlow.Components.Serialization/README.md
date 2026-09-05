# FluxFlow.Components.Serialization

Standalone explicit content conversion nodes.

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

Install `FluxFlow.Components.Serialization.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
