# Native Dataflow Core

The agreed boundary moves delivery guarantees out of ordinary component outputs.
Consumers choose buffering; stronger retention/acknowledgment requirements belong
to explicitly selected components. No persistence component was added in this step.

## Implementation

- FlowTransformNode links its native TransformManyBlock directly to BroadcastBlock.
  FlowNode remains a small specialization for explicit streaming/time-based emissions.
  Explicit error forwarding shares its emission path to preserve sequential ordering.
- FlowSource and side outputs use native BroadcastBlock. Removed FlowOutput,
  FlowOutputOptions, FlowOutputOffer, FlowOutputDispatch and FlowRouter.
- Application attaches native LinkTo connections with optional predicates. It no
  longer creates a dispatch adapter, subscription queue or application link capacity.
- Lifetimes coordinate native blocks; custom output disposal/drain callbacks are
  gone. A small internal helper shares control-block construction and local emission.
- Fan-in completion, control/reply ordering, cancellation, failed-build cleanup,
  originating failure identity and standard DI ownership remain.
- Event registration exposes one named output (Events by default); the unused
  parallel activity stream and signal registration flag are removed.
- Core component/port registration takes ComponentAddress. Link takes PortAddress,
  and FlowLink retains typed addresses. PortAddress exposes its ComponentAddress.
  Canonical Value strings are used at native DI lookup boundaries. Configuration
  passes its already validated values directly without reparsing them.
- Request/reply matching, message/value representation, lineage, ActivitySource,
  Meter and processing exception diagnostics retain their existing responsibilities.

## Observable contract and migration

This changes delivery semantics intentionally. Awaiting publication acknowledges
local output admission, not receipt by all targets. OutputCapacity sets native
BroadcastBlock admission capacity; it no longer bounds reliable downstream retention.
A full/declining target may miss messages without faulting the producer. A broadcast
block retains its latest value and may offer it to a late subscriber. Fanout shares
the original message and value rather than cloning them.

Native predicates run as part of native source offers. False predicates filter that
link; thrown predicates fault the source. Earlier accepted branches are not rolled
back. Custom components exposing BufferBlock/TransformBlock retain native routing
and completion behavior; Application does not secretly broadcast or drain them.

Replace string arguments to Core registration/Link with ComponentAddress and
PortAddress, preferably reusing component.Port(name). Remove linkCapacity arguments
and signal registration flags. Use BroadcastBlock for owned side outputs. Dependent
packages must be rebuilt against the updated public API baseline.

Tests specific to the removed output protocol were removed. Native broadcast cases
cover full/declining consumers, healthy siblings, unchanged identity, filtering,
predicate failure, retained latest values and completion. Processing error ordering,
tracing, request/reply, fan-in and lifecycle tests remain. Capture sinks have enough
receiving capacity where the test requires every message; slow-consumer tests
explicitly assert the new publication contract.

## Validation

- Full Release solution: 1,956 passing test executions across .NET 8 and .NET 10;
  no warnings.
- Isolated Core package consumer: passed both frameworks, including native blocks,
  side outputs, typed values, error recovery and completion.
- Isolated Configuration package consumer: passed both frameworks, including
  component setup, expression engines and configured link construction.
- Public API source-declaration baseline updated; whitespace validation clean.

No performance claim is made without fresh measurements. Native block semantics
do not establish durability or exactly-once delivery. Telemetry exporters and
further message serialization changes are outside this refactoring.
