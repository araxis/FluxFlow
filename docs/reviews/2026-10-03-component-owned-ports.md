# Component-owned ports and composed lifetimes

## Decision

FlowNode remains an optional helper for the main input, Output, Events and message
processing. It no longer constructs or configures arbitrary additional ports.
Exceptional components create their native Dataflow blocks and supply them to a
per-component lifetime coordinator. There is no global port registry or new base class.

## Changes

- Removed FlowNode.AddInput, AddOutput<T>, AddControlInput, the port gate, exposure
  flag and mutation guard. Reading Input or Output no longer changes node state.
- CreateInput returns the existing FlowProcessingLifetime; RunOnStart returns the
  existing FlowSourceLifetime. Ordinary components can ignore the result.
- These two existing coordinators are public and accept component-created blocks.
  They do not create ports, configure capacity, attach addresses or establish links.
  Processing and independently started production retain their different shutdown phases.
- Gate creates ordinary ActionBlock commands; Router creates broadcast route outputs;
  Retry and RateLimit create their broadcast requests and terminal reply ActionBlocks.
- ProcessMessageAsync shares tracing, finite-result materialization and error conversion
  with caller-created processing blocks. ProcessControlAsync shares awaited control
  tracing and owner faulting without constructing a block.
- Reply registration accepts native targets of FlowMessage, matching IFlowNode's
  terminal-input contract. Coordinators reject registration after closing; source
  registration also closes at startup. Supply all blocks before executing work.
- Runtime synchronization remains inside the lifetime coordinators where faults,
  cancellation and asynchronous completion can race. FlowNode construction has no lock.

## Preserved behavior

Ordinary inputs drain before flush. Flush failures fault the component; returned
flush sequences are materialized before publication. Outputs finish before terminal inputs close, so accepted
business work can still receive replies. Faults cancel active work and preserve the
initiating error. Sources distinguish production completion from later control
completion, retaining a separate control cancellation token. Resource cleanup runs
once after work ends. Native broadcast admission and consumer responsibility remain.

No component settings, configuration syntax, DI registration or Application execution
policy changed. Existing direct IFlowNode implementations remain supported; lifetime
reuse does not require FlowNode inheritance.

## Migration and validation

This is a breaking authoring API change. Replace base additional-port factory calls
with native block construction and registration on the returned coordinator. Main
CreateInput/RunOnStart calls in ordinary components need no source change. Recompile
dependent packages because their return signatures changed. The public API baseline,
authoring guides, tests and isolated Application consumer are updated.

The full Release suite passes 2,118 test executions across .NET 8 and .NET 10 with
zero warnings. Coverage includes direct coordinator composition without FlowNode,
late registration rejection, reply availability during drain, fault/cancellation
identity, additional-input ordering, source shutdown and control trace context.
Isolated Core, Application, Routing, Resilience and RateLimit package consumers pass
on both targets. The Application consumer exercises an externally authored component
with an explicitly constructed additional broadcast output. Whitespace validation passes.

No commit or remote publication was performed.
