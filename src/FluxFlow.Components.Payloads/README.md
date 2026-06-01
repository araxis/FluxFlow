# FluxFlow.Components.Payloads

Reusable payload inspection components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `payload.inspect` | `Input` -> `Output` | Classifies byte or text payloads and emits preview metadata. |

## Payload Inspect

```json
{
  "type": "payload.inspect",
  "name": "inspect",
  "maxPreviewBytes": 1024,
  "maxFormattedChars": 4096,
  "detectBase64": true,
  "formatJson": true,
  "formatXml": true,
  "boundedCapacity": 128
}
```

`payload.inspect` consumes `PayloadInspectionRequest` values and emits
`PayloadInspectionResult` values. It can classify empty, JSON object, JSON
array, JSON scalar, XML, base64, text, and binary payloads. For byte payloads,
an explicit encoding hint wins; otherwise a content type `charset` value is
used when present.

Inspection results include byte count, detected encoding, text preview,
formatted preview, parse error text when applicable, and truncation flags.
Malformed JSON or XML remains a normal inspection result. Unexpected
processing failures are emitted as `FlowError` values and the node continues
with later messages.

## Request

```csharp
new PayloadInspectionRequest
{
    Bytes = payloadBytes,
    ContentType = "application/json",
    EncodingHint = "utf-8"
};
```

Hosts can adapt any domain envelope into `PayloadInspectionRequest`. The
package does not include transport-specific fields.

## Registration

```csharp
registry.RegisterPayloadComponents();
```

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
