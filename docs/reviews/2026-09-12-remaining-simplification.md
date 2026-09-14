# Remaining simplification review after step 24

This is a targeted source review, not full repository qualification. No runtime
code changed in this review, and no new build or test run was needed.

## What the recent steps achieved

Storage, FileSystem, Sessions, Metrics, Projections, State and Expectations now
process canonical envelopes alongside local typed requests rather than constructing
typed transport envelopes. Several trivial materializer subclasses were removed;
HTTP and MQTT body conversion avoid reconstructing entire requests as mutable JSON
trees. State, HTTP and MQTT still expose custom materializers as public extension
points. Typed domain models remain useful inside components.

The direct workflow API is already small: FlowWorkflow delegates component addition
to FlowTopology. FlowApplication coordinates topology, execution and request/reply.
Those responsibilities should be judged by their behavior, not collapsed merely
to reduce the number of files.

## Ranked opportunities

1. Make Composition port metadata reflect canonical transport.

   ComponentInputPort and ComponentOutputPort already expose blocks of FlowMessage.
   Their MessageType is fixed to FlowValue, except the signal marker uses object.
   ComponentPortMetadata still accepts arbitrary Type arguments, generic Create<T>
   factories and type-oriented deconstruction. RuntimeComponentRegistrationBuilder
   repeatedly constructs metadata with Create<FlowValue>. ComponentActivation then
   compares physical ports against these metadata types.

   Proposed first implementation: introduce a canonical metadata construction path
   and migrate internal registration to it, preserving name, kind, cardinality and
   input shape. Keep validation for externally supplied legacy metadata until its
   migration is complete. Do not confuse a control-port marker with business-payload
   typing. The sibling UI has direct MessageType consumers, so wholesale public
   removal requires a coordinated migration; no sibling edits were made here.

   Benefit: component registration no longer asks developers to select a payload
   type already fixed by the runtime. This is the recommended next bounded step.

2. Reduce repeated component delegation only if the resulting authoring is simpler.

   StorageGetNode and its siblings still forward Ports, Input, Output, Events,
   Completion, Complete, Fault and DisposeAsync to a family-local pipeline. Those
   pipelines also serve real purposes: local parsing, rejection mapping and lifetime
   integration. Replacing them with another generic base could simply move the
   complexity. Compare one concrete component before/after first; preserve native
   ports, disposal ownership, cancellation and session finalization. Do not create
   a universal deserializer or a new context object to solve this repetition.

3. Revisit diagnostic plumbing in the already planned observability phase.

   ComponentInstance supports both legacy events and named/addressable event bridges,
   while components emit domain events themselves. There may be consolidation work,
   but source addressing, best-effort event delivery, completion and control ordering
   are actual contracts. Preserve these during phase 1. Standard logs, traces and
   metrics belong in phase 2, followed by host/SSE integration in phase 3.

## Boundaries worth retaining

- FlowTopology owns names, addresses and graph validation.
- FlowExecution and lifetime code own bounded links, startup, drain and cleanup.
- FlowRequestReply owns bounded pending exchanges and correlation.
- Domain models and local parsing allow each component to execute its own operation.
- Timer-driven Expectations requires one-time resolution coordination; removing the
  lock/queue without an equivalent tested protocol would remove correct behavior.

Phase 1 is still incomplete. The review does not claim a measured speedup or a
repository-wide line reduction; the accumulated working tree contains earlier
refactoring and UI extraction, so an unqualified diff total would be misleading.

## Metadata follow-up after step 25

Production source/sample searches found no remaining canonical generic factory
callers beyond the preserved public factory implementations. Internal registration
now uses Create(name). The remaining dynamic constructor in ApplicationLinkCompiler
copies ApplicationSystemOutputMetadata.MessageType and must not discard it.

The older API supports more than inspection: ApplicationLinkDefinition carries a
declared type and adapts typed predicates; the standalone compiler checks declared,
source and destination types. ComponentConfigurationPreflight rejects noncanonical
hosted data ports, and ComponentActivation validates physical ports against their
descriptor. Removing these checks while accepting legacy metadata would weaken
validation. The separate UI still inspects MessageType. This narrows the initial
recommendation: the canonical authoring path was redundant, but not every remaining
type-related branch is dead code.

Four canonical metadata fixture sites now use Create(name), exercising it through
input-shape and link-compiler paths. Typed mismatch and explicit compatibility tests
retain the legacy APIs. All 273 Composition tests passed on net10.0. No production
changes or public removals were made in this follow-up. Further type-API removal is
a contract migration, not an automatic continuation of the internal cleanup.
