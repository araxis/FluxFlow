# Architecture

## Overview

FluxFlow.Engine is a small library that bridges a **declarative JSON definition** to a
**live TPL Dataflow graph**. The three layers are deliberately separate:

```
┌───────────────────────────────────────────┐
│  Definitions layer                        │
│  ApplicationDefinition, NodeDefinition,   │
│  WorkflowDefinition, LinkDefinition       │
│  (plain records, JSON-serializable)       │
└───────────────────────┬───────────────────┘
                        │  ApplicationRuntimeBuilder.Build()
                        ▼
┌───────────────────────────────────────────┐
│  Runtime layer                            │
│  ApplicationRuntime, Workflow,            │
│  RuntimeNode, InputPort<T>, OutputPort<T> │
│  (live Dataflow graph, owned by host)     │
└───────────────────────┬───────────────────┘
                        │  IFlowNode.StartAsync()
                        ▼
┌───────────────────────────────────────────┐
│  Node layer                               │
│  IFlowNode implementations                │
│  (user code — MQTT, HTTP, files, …)       │
└───────────────────────────────────────────┘
```

## Definitions layer

`ApplicationDefinition` is a plain C# record that is JSON-serializable.
It contains:

- **`Resources`** — shared nodes (one instance across all workflows), e.g. broker connections.
  Keyed by name, stored under `"resources"` in JSON.
- **`Workflows`** — named groups of processing nodes. Each workflow is a directed graph.
  Stored as `"workflows": { "main": { "node1": {...}, "node2": {...} } }`.
- **`Dashboards`** — optional UI layout definitions (grid-based).
- **`Tests`** — named scenarios for deterministic test execution.

Nodes in both resources and workflows use the same `NodeDefinition` shape:

```json
{
  "type": "my.source",
  "phase": 0,
  "configuration": { "host": "localhost", "port": 1883 },
  "Input": "upstream.Output"
}
```

`type` is a string node-type key, `phase` controls start order,
`configuration` is free-form JSON passed to the factory, and any other
properties are treated as **port links** (e.g. `"Input": "upstream.Output"`).

## Runtime layer

`ApplicationRuntimeBuilder.Build(definition)` turns the definition into a live graph:

1. **Validate** — `ApplicationDefinitionValidator` checks names, links, and type presence.
2. **Create nodes** — for each node name, looks up the registered `RuntimeNodeFactory` and
   calls it with a `RuntimeNodeFactoryContext`. The factory returns a `RuntimeNode`.
3. **Link ports** — reads port-link declarations from each `NodeDefinition`. For each link,
   finds the named `OutputPort<T>` on the source and the named `InputPort<T>` on the target.
   Type safety is enforced: `OutputPort<T>.TryLinkTo(InputPort<T>)` rejects type mismatches
   with a structured build error.
4. **Drain unlinked outputs** — unlinked `OutputPort<T>` with `DrainWhenUnlinked = true`
   are wired to `DataflowBlock.NullTarget<T>()` so their sources can complete.
5. **Identify entry nodes** — nodes that have no incoming links are "entry nodes".
   When `ApplicationRuntime.Complete()` is called, it propagates completion starting
   from these entry nodes.

## Port model

Every `OutputPort<T>` wraps its inner `ISourceBlock<T>` with a `BroadcastBlock<T>`:

```
ISourceBlock<T>  ──→  BroadcastBlock<T>  ──→  InputPort<T>.Target (ITargetBlock<T>)
                                           └──→  InputPort<T>.Target (second consumer)
                                           └──→  NullTarget<T>       (if unlinked)
```

This means a single output can feed many inputs without changing any node code.
The `PropagateCompletion` flag on the broadcast→target link is managed by the builder;
when a target has multiple sources, a coordination wrapper (`InputCompletionLink`) ensures
the target completes only after **all** sources have completed.

## Completion model

```
entry nodes ──Complete()──→ PropagateCompletion ──→ downstream nodes ──→ … ──→ terminal nodes
                                                        └──→ Workflow.Completion
                                                               └──→ ApplicationRuntime.Completion
```

Calling `ApplicationRuntime.Complete()` signals all entry nodes. Completion then
flows through the Dataflow link chain until all nodes are done.
`ApplicationRuntime.Completion` is `Task.WhenAll(all node.Completion tasks)`.

## State machine

Both `ApplicationRuntime` and each `Workflow` track state using a lock-guarded field
and publish transitions as `ISourceBlock<ApplicationStateChanged>` /
`ISourceBlock<WorkflowStateChanged>`:

```
Idle → Starting → Running → Stopping → Stopped
                ↘                    ↗
                  Faulted ──────────
```

State transitions never block; subscribers receive every change via `LinkTo`.

## Event aggregation

`ApplicationRuntime.Events` is an `ISourceBlock<FlowEvent>` that collects events
from every node that implements `IFlowEventSource`. The internal `FlowEventCollector`
links each node's `Events` block to a single broadcast and completes it when
`ApplicationRuntime.Completion` finishes.

## Error isolation

`IFlowNode.Errors` is `ISourceBlock<FlowError>`. When a node processes a message and
encounters an error, it posts a `FlowError` to this block rather than throwing.
Nothing outside the node is disrupted; subscribers can link to the error block to
observe, route, or log failures.
