# FluxFlow.Components.Payloads

A standalone payload-inspection node for FluxFlow.

## What it is

`PayloadInspectNode` is a self-contained TPL Dataflow processor. You post
`PayloadInspectionRequest`s to its input and it broadcasts
`PayloadInspectionResult`s on its output (failures on the error port, a
diagnostic note on the event port). It needs **nothing else** — no engine,
registry, or runtime:

```csharp
await using var node = new PayloadInspectNode();

node.Output.LinkTo(logger.Input);   // broadcast: link the output to as many
node.Output.LinkTo(mapper.Input);   // downstream nodes as you like

await node.Input.SendAsync(FlowMessage.Create(new PayloadInspectionRequest
{
    Text = """{"name":"flux"}""",
    ContentType = "application/json"
}));
```

Messages travel as `FlowMessage<T>` envelopes, so the correlation id flows from
the request through to the result for free.

## Ports

| Port | Block | Purpose |
|------|-------|---------|
| `Input` | `BufferBlock<FlowMessage<PayloadInspectionRequest>>` | bounded intake — `SendAsync` applies backpressure |
| `Output` | `BroadcastBlock<FlowMessage<PayloadInspectionResult>>` | the inspection result, fanned out to every linked consumer |
| `Errors` | `BroadcastBlock<FlowError>` | unexpected processing failures and unsupported encoding hints |
| `Events` | `BroadcastBlock<FlowEvent>` | `payload.inspect.inspected` / `payload.inspect.failed` notes |

Outputs are broadcast (latest-wins, no backpressure): a consumer that keeps up
sees every message; one that falls badly behind may miss some. That is the
deliberate trade for simplicity. If a graph genuinely must not drop, bridge that
edge through its own bounded buffer.

## What it does

The node consumes `PayloadInspectionRequest` values and emits
`PayloadInspectionResult` values. It can classify empty, JSON object, JSON
array, JSON scalar, XML, base64, text, and binary payloads. For byte payloads,
an explicit encoding hint wins; otherwise a content type `charset` value is
used when present.

Inspection results include byte count, detected encoding, text preview,
formatted preview, parse error text when applicable, and truncation flags.
Malformed JSON or XML remains a normal inspection result. An unsupported
encoding hint (and any other unexpected processing failure) is emitted as a
`FlowError` carrying the request's correlation id, and the node continues with
later messages.

Payloads larger than `maxInputBytes` (default 1048576) are not classified or
formatted. They still emit a normal `PayloadInspectionResult` with the byte
count and a "payload too large" formatted preview, not an error.

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

## Options

```csharp
new PayloadInspectOptions
{
    MaxInputBytes = 1_048_576,  // payloads past this skip classification/formatting
    MaxPreviewBytes = 1024,     // text-preview byte cap
    MaxFormattedChars = 4096,   // formatted-preview char cap
    DetectBase64 = true,
    FormatJson = true,
    FormatXml = true,
    BoundedCapacity = 128       // input buffer size
};
```

Pass a `TimeProvider` as the second constructor argument to control the clock
used for result/event/error timestamps (defaults to `TimeProvider.System`).

## Composition

Building a workflow — reading config, creating nodes, linking them — is a
separate concern from the node. This package is just the node; wire it from
whatever composition/host layer you use (`appsettings.json` → construct nodes →
`LinkTo`).
