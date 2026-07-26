# Flow Data Contracts

Status: vNext foundation contract.

## FlowValue

`FlowValue` is a deeply immutable discriminated value. It supports null,
Boolean, `BigInteger`, decimal, double, string, binary, `DateTimeOffset`,
`DateOnly`, `TimeOnly`, `TimeSpan`, `Guid`, arrays, and objects.

- Array order is significant.
- Object property order is insignificant.
- Object names use ordinal, case-sensitive comparison.
- Integer, decimal, and floating-point kinds are never equal to each other.
- Non-finite floating-point values are rejected.
- Mutable constructor inputs are copied.
- Arrays, objects, and binary values are exposed through immutable collections.
- A Dataflow broadcast may share one `FlowValue` instance without cloning.

`FlowValueCanonicalJson` is deterministic and lossless. Every value is encoded
as an object containing `kind` and, except for null, `value`. Object properties
are written in ordinal order. Numeric payloads use invariant strings so number
kinds and large integers survive a round trip. This format is for persistence,
identity, and tests; it is separate from natural JSON used by mapping.

## FlowContent

`FlowContent.FromBytes(...)` copies ingress bytes once and preserves content
type and encoding metadata. `FlowContent.FromValue(...)` represents content
created from an existing logical value and therefore has no original transport
representation.

`ReadAsFlowValue(...)` decodes on first use under a thread-safe gate. The value
or captured exception is cached, so concurrent branches cannot repeat parsing.
The first codec catalog used for byte-backed content defines that decode.

`FlowContentCodecCatalog` resolves codecs in this order:

1. exact normalized media type.
2. structured suffix, such as `+json`.
3. media family, such as `text/*`.
4. binary fallback.

The default catalog supports `application/json`, `+json`, `text/*`, and binary
fallback. Unsupported or invalid text encodings fall back to UTF-8. XML mapping
is intentionally not invented in the foundation package; the Serialization
family will register its explicit XML convention in its own migration pass.

## Message Identity

`FlowMessage<T>` remains generic and is still owned by `FluxFlow.Nodes`.

- `TraceId` identifies one end-to-end workflow processing lineage and remains
  stable as data moves through nodes.
- `MessageId` identifies one particular emitted envelope or processing hop.
- `CausationId` identifies the envelope that caused the current envelope.
- `CorrelationId` is optional external, business-conversation, or protocol
  metadata. It remains available for compatibility but is not the default
  internal workflow coordination key.
- Headers are `IReadOnlyDictionary<string, FlowValue>` with ordinal keys.

`FlowMessage.Create(...)` creates missing correlation, trace, and message
identities. `With(...)` preserves correlation, trace, and headers; creates a new
message id and timestamp; and sets causation to the source message id.

Workflow Ack/Nak, timeout, and retry coordination use `TraceId` by default.
Specialized protocol adapters may use `CorrelationId` when an external contract
requires it. Retry attempts additionally use an internal attempt discriminator,
such as `(TraceId, Attempt)`, so late feedback cannot settle a newer attempt.
That discriminator is an implementation header and does not replace the
workflow-visible `TraceId`.

## Results

`IFlowResult` defines `Kind`, computed `IsError`, optional `FlowError`, and
`Timestamp`. `FlowResult<T>` is the simple operation implementation. Families
with multiple commands may define polymorphic roots implementing the same
contract.

`FlowError` contains stable code, message, category, transient classification,
and `FlowValue` details. It intentionally excludes raw exceptions from normal
workflow data. Runtime exceptions belong in protected diagnostics and system
events.

Canonical component packages use normal result output for expected failure
data. Legacy compatibility packages may retain their established diagnostic
streams, while unrecoverable implementation and lifecycle faults remain on
component completion.
