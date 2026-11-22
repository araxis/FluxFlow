# Message construction and identity copies

Typed Create and WithValue previously constructed a common FlowMessage and then
constructed FlowMessage<T> from it. They now construct the final typed record
directly, using the same internal construction rules as common messages.

Three operations remain distinct inside the existing records:

- Initial construction generates identity and captures the creation context.
- Derivation starts from the input, generates a new message ID/timestamp, records
  causation and captures the current processing context.
- Identity-preserving copies use the native C# record copy constructor. Payload,
  headers or request correlation can change without generating identity, changing
  timestamp or recapturing an unrelated ambient context.

Typed projections, event address enrichment and request/reply now share that copy
path. Their callers no longer enumerate message ID, trace ID, timestamp, correlation
and causation manually. FlowEvent creates its final message with the event timestamp
directly, avoiding temporary envelopes previously used only to replace timestamp.

There is no new envelope/context type, factory service or public helper. Public
Create/WithValue signatures, header snapshots, lazy payloads, serialized JSON and
error semantics remain unchanged. The source-declaration API baseline passes without
an update. Typed deserialization still uses the canonical reader before projection,
retaining one strict envelope-validation implementation.

## Validation

- Full Release solution: 1,976 passing executions across .NET 8 and .NET 10,
  with no warnings.
- New tests cover common/typed creation and derivation, lazy payloads, trace context,
  explicit causation and headers, null-payload rejection and identity-only projection.
- Existing event, request/reply, serialization, source-generated metadata and
  processing-context tests pass.
- Isolated Core package consumer passes on both target frameworks.

The removed allocations are visible in the construction paths; no throughput or
latency improvement is claimed without a separate benchmark.
