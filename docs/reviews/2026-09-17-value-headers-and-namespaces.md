# Structured headers and folder-aligned namespaces

FlowMessage.Headers and every header parameter now use
IReadOnlyDictionary<string, FlowValue>. The ordinal immutable dictionary holds
component-owned values by reference; inheriting headers or adding tracing context
does not serialize unrelated values. FlowValue implementations must uphold their
immutable-value contract; a read-only dictionary cannot freeze mutable CLR objects.

JSON header values can be strings, numbers, booleans, arrays, objects or explicit
null (JsonFlowValue.Null). Deserialization produces JsonFlowValue values rather
than reconstructing arbitrary CLR types. Existing string-only JSON remains valid.
A small string-to-FlowValue implicit conversion keeps text header initialization
simple. It produces a JSON string; no reverse implicit conversion was introduced.

Tracing parses only string-valued traceparent/tracestate entries. Existing tracing
capture/replacement rules remain intact, and business headers stay lazy. Runtime
events use FlowValue headers and retain envelope sharing when application/revision
text is already identical. Request/reply and nested function correlation retain
their existing behavior. No automatic output-to-header capture setting was added.

## Namespaces

The user requested namespace cleanup after FlowMessage alone had been manually
moved. All Core production source declarations now follow the project root plus
their physical folder:

| Folder | Namespace |
| --- | --- |
| Messaging | FluxFlow.Core.Messaging |
| Nodes | FluxFlow.Core.Nodes |
| Application | FluxFlow.Core.Application |
| Ports | FluxFlow.Core.Ports |
| Coordination | FluxFlow.Core.Coordination |
| Configuration/Json/Model | FluxFlow.Core.Configuration.Json.Model |
| Runtime/Ports | FluxFlow.Core.Runtime.Ports |
| Runtime/Signals | FluxFlow.Core.Runtime.Signals |

The same rule applies to all other Core folders. Routing already matches its
project namespace. Test/benchmark project identities and package IDs are unchanged.
No forwarding namespaces or projects were added. Source callers, fixtures, current
guide examples and package consumers were migrated. Historical reviews retain the
names used at their original checkpoints. Public API baseline updated.

This is a breaking preview change: migrate imports and header dictionaries, then
rebuild consumers. Read text headers explicitly via value.Json.GetString().

## Validation

Release build: zero warnings/errors. All 2,422 tests passed in nine final reports.
Final regression and hashes are recorded in
artifacts/value-headers-20260917/verification.json. New checks cover structured JSON
round-trips, typed header identity across branches, no eager JSON conversion,
tracing alongside lazy business headers, and namespace/folder alignment. The
existing source-generated JSON test now includes a structured header.

Fresh package-only Core and Routing consumers pass .NET 8 and .NET 10. Core also
checks structured headers through lineage generation and JSON round-trip.
Intermediate failures were migrated string assertions and old namespace/document
expectations; final qualification is kept separately from those attempts.

No commit, push or publication.
