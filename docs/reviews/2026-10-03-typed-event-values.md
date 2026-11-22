# Typed event values

FlowEvent previously serialized immediately into JsonFlowValue during publication,
then parsed that JSON to provide a FlowEvent to a consumer. It now derives from
FlowValue; ToMessage stores the instance, and typed projection returns it directly.
The inherited lazy JSON implementation performs one conversion when requested.

Core still has no OpenTelemetry SDK dependency. Native Events ports carry FlowMessage;
Application stamps source headers without replacing the value. Consumers select logging,
file, dashboard or telemetry handling through ordinary links.

## Data and envelope

CorrelationId belongs to FlowMessage. Replace an event CorrelationId initializer with
ToMessage(correlationId), and read it from the received envelope. Sharing one event across
different envelopes cannot change its data or introduce contradictory correlations.
Event time is captured at construction unless explicitly supplied. ToMessage uses it
for envelope time; ordinary FlowMessage.Create can use a separate envelope time.

Name validation/normalization and measurement validation happen during initialization.
Attributes and Dimensions copy their dictionaries and snapshot mutable/unknown objects
into owned JsonElements. Immutable scalar values and FlowValues retain their CLR types.
Measurements use an immutable dictionary. Nested JSON projection uses JsonElement
instead of constructing mutable dictionaries/arrays and serializing them again.

FlowEvent changes from record to class: no record copying or value equality. Existing
wire fields and Type/Kind/Level headers are retained. Attributes, grouping Dimensions,
numeric Measurements and typed Details serve different consumer uses; collapsing them
would need a separate migration and is unnecessary for typed event delivery. Headers
support routing without inspecting the payload. This change introduces no event bus,
exporter interface, logger adapter or additional dependency.

## Validation

Regression coverage checks typed identity and delayed JSON, concurrent readers, envelope
correlation, full message JSON round trips, borrowed JSON ownership, nested mutable input
snapshots, immutable exposed maps, finite measurements and Application event forwarding.
The isolated Core package consumer also exercises typed event publication and JSON.
Unsupported attribute values retain invariant textual fallback, including a FlowValue
whose deferred JSON conversion fails; Details still follows the ordinary FlowValue contract.

Final verification: all 2,098 Release test executions passed across 28 project/target
runs. The Debug solution build passed for 32 projects with zero warnings or errors.
The public API baseline is updated and passing. The isolated Core package consumer
passed on .NET 8 and .NET 10. Whitespace validation passed. No commit or publication.
