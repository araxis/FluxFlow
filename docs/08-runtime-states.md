# Runtime States

FluxFlow exposes state at three levels:

| Level | Type | How to observe |
|-------|------|----------------|
| Host | `FlowApplicationHostState` | Read `FlowApplicationHost.State`. |
| Application runtime | `ApplicationState` | Read `ApplicationRuntime.State` or link to `StateChanges`. |
| Workflow | `WorkflowState` | Read `Workflow.State` or link to `StateChanges`. |

The host state is a lifecycle wrapper around loading, build, start, stop, and
cleanup. Runtime and workflow states describe the live graph after it is built.

## Host State

`FlowApplicationHost.State` is a snapshot property. It does not have a state
change stream.

| State | Meaning |
|-------|---------|
| `Empty` | No runtime is currently built. This is the initial state and the state after a failed build. |
| `Built` | `Build()` succeeded and `Runtime` is available, but nodes have not started yet. |
| `Running` | `StartAsync()` or `StartBuiltAsync()` started the built runtime successfully. |
| `Stopped` | Startup was canceled, stop completed, or `StopAsync()` was called when no runtime was present. |
| `Faulted` | Startup or stop failed. Check `LastException` and `LastBuildResult`. |

Common host transitions for a new host:

| Operation | Success path | Failure path |
|-----------|--------------|--------------|
| `Build()` | `Empty -> Built` | `Empty` |
| `StartAsync()` | `Empty -> Built -> Running` | `Empty` for build failure, `Faulted` for start failure |
| `StartBuiltAsync()` | `Built -> Running` | `Faulted` for start failure |
| canceled start | `Built -> Stopped` | rethrows `OperationCanceledException` |
| `StopAsync()` | `Running -> Stopped` | `Faulted` for non-cancel failures |
| `Dispose()` / `DisposeAsync()` | releases runtime resources | no new state is set |

`StartAsync()` always builds first. Use `Build()` plus `StartBuiltAsync()` when
the application needs to inspect the runtime or link to streams before startup.
Calling `Build()` again disposes any existing runtime before creating the next
one.

```csharp
await using var host = FlowApplicationHost.Create(definition, registry);

var build = host.Build();
if (!build.IsSuccess || host.Runtime is null)
    return;

var stateChanges = new BufferBlock<ApplicationStateChanged>(
    new DataflowBlockOptions { BoundedCapacity = 128 });

host.Runtime.StateChanges.LinkTo(
    stateChanges,
    new DataflowLinkOptions { PropagateCompletion = true });

await host.StartBuiltAsync();
```

## Application Runtime State

`ApplicationRuntime.State` starts at `Idle`. `StateChanges` publishes
`ApplicationStateChanged` records for transitions after the runtime is built.

`ApplicationStateChanged` contains:

- `Previous`
- `Current`
- `Exception`
- `OccurredAt`

Application runtime states:

| State | Meaning |
|-------|---------|
| `Idle` | Runtime is built but startup has not begun. |
| `Starting` | Runtime is starting nodes in phase order. |
| `Running` | All nodes started successfully. |
| `Stopping` | `Complete()` was called and entry nodes were asked to complete. |
| `Stopped` | All node completions finished without fault, or startup was canceled. |
| `Faulted` | Startup, node completion, or explicit `Fault()` put the runtime in a failed state. |

Common runtime transitions:

```text
Idle -> Starting -> Running -> Stopping -> Stopped
Idle -> Starting -> Faulted
Idle -> Starting -> Stopped   // canceled startup
Running -> Faulted            // node completion fault or explicit Fault()
```

When `StartAsync()` succeeds, the runtime starts a completion watcher. When all
nodes complete, the watcher moves the runtime to `Stopped` or `Faulted`.

If the runtime is already `Faulted`, later completion cannot overwrite it with
`Stopped`.

## Workflow State

Each `Workflow` has its own state. Workflow state uses the same lifecycle names
as the application runtime:

| State | Meaning |
|-------|---------|
| `Idle` | Workflow exists but startup has not begun. |
| `Starting` | Workflow nodes are being started. |
| `Running` | Workflow startup completed. |
| `Stopping` | Workflow entry nodes were asked to complete. |
| `Stopped` | Workflow node completions finished without fault, or startup was canceled. |
| `Faulted` | Workflow startup, completion, or explicit `Fault()` failed. |

`WorkflowStateChanged` contains:

- `WorkflowName`
- `Previous`
- `Current`
- `Exception`
- `OccurredAt`

The application runtime coordinates workflow startup when you start the full
runtime. Workflows are marked `Starting` before node startup begins and
`Running` after all nodes start successfully.

## Startup Order

Runtime startup is phase ordered. Resources and workflow nodes are grouped by
their `Phase`, then started from the lowest phase to the highest phase.

If any node throws during startup:

1. Runtime state becomes `Faulted`.
2. Workflow states become `Faulted`.
3. The runtime asks all nodes to fault.
4. `ApplicationRuntime.StartAsync()` throws `ApplicationRuntimeNodeStartException`
   when the failing node address is known.
5. `FlowApplicationHost.StartAsync()` catches startup failures, records a
   `StartFailed` host error, disposes the failed runtime, and returns an
   unsuccessful build result.

If startup is canceled:

1. Runtime state becomes `Stopped`.
2. Workflow states become `Stopped`.
3. Started nodes are not faulted.
4. Host state becomes `Stopped`.
5. The cancellation is rethrown.

## Completion And Stop

`ApplicationRuntime.Complete()` moves the runtime to `Stopping`, completes
resource entry nodes, and completes each workflow.

`Workflow.Complete()` moves the workflow to `Stopping` and completes workflow
entry nodes.

Completion becomes final only when node `Completion` tasks finish:

- successful completion moves state to `Stopped`
- faulted completion moves state to `Faulted`

`FlowApplicationHost.StopAsync()` calls runtime `Complete()` and waits for
runtime completion. If waiting succeeds, host state becomes `Stopped`. If a
non-cancel exception is thrown while stopping, host state becomes `Faulted` and
`LastException` is set.

## State Streams

State streams are transition streams, not event history. Subscribe before
startup if the application needs `Starting` or `Running` transitions.

```csharp
var build = host.Build();
if (!build.IsSuccess || host.Runtime is null)
    return;

var runtimeStates = new BufferBlock<ApplicationStateChanged>(
    new DataflowBlockOptions { BoundedCapacity = 128 });

host.Runtime.StateChanges.LinkTo(
    runtimeStates,
    new DataflowLinkOptions { PropagateCompletion = true });

foreach (var workflow in host.Runtime.Workflows)
{
    workflow.StateChanges.LinkTo(
        new ActionBlock<WorkflowStateChanged>(change =>
            Console.WriteLine($"{change.WorkflowName}: {change.Current}"),
            new ExecutionDataflowBlockOptions { BoundedCapacity = 128 }),
        new DataflowLinkOptions { PropagateCompletion = true });
}

await host.StartBuiltAsync();
```

Late subscribers should read the current snapshot property first:

```csharp
var currentRuntimeState = host.Runtime?.State;
var currentHostState = host.State;
```

## Dashboard Pattern

For application dashboards:

1. Show `FlowApplicationHost.State` as the outer host status.
2. Show `ApplicationRuntime.State` as the current graph status when a runtime
   exists.
3. Show each `Workflow.State` beside workflow-level diagnostics and events.
4. Link to state streams before startup for live transition rows.
5. Read `LastBuildResult` and `LastException` when the host is `Faulted`.
6. Treat dispose as cleanup, not as a separate status.

This keeps dashboard status aligned with the engine lifecycle without forcing
application-specific UI rules into the engine.
