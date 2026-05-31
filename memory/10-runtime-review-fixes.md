# Runtime Review Fixes

Date: 2026-05-31

## Decisions

- Keep fanout as the default output concept for workflow ports.
- Make default fanout reliable: each item is sent to every linked input before the output advances to the next item.
- Keep component implementations stable. Nodes still expose `ISourceBlock<T>` and `ITargetBlock<T>` ports.
- Keep latest-value delivery out of the default workflow path. It can be added later as an explicit output mode.
- Do not expose the raw wrapped source from `OutputPort<T>`; runtime delivery owns fanout semantics.

## Changes

- Replaced the internal output `BroadcastBlock<T>` wrapper with a bounded queue and a runtime-owned fanout pump.
- Made the fanout pump start lazily after the first linked input or discard drain is registered, preserving values buffered before graph wiring finishes.
- Added per-link cancellation so disposing a link releases any pending send to a full input.
- Added output-port disposal and owner-side pump cancellation so runtime cleanup can stop output pumps even when the wrapped source never completes.
- Made multi-source input completion coordination safe to dispose without faulting the target input during cleanup.
- Stopped source-completion watchers from faulting target inputs when source failure is observed after cleanup has already started.
- Made workflow and application runtime disposal best-effort. Every link, output port, event collector, and node is given a cleanup attempt, then errors are thrown as one aggregate.
- Made failed-start runtime disposal best-effort in the host. The original start result is still returned and cleanup errors are appended to the result.
- Preserved cancellation semantics during runtime and host startup: cancellation stops startup, clears the failed runtime, and does not run node fault hooks.
- Made dataflow-backed helper nodes run fault hooks inside the synchronous `Fault(...)` call so runtime fault cleanup can observe hook failures and final diagnostics.
- Moved completion-state fault preservation into the runtime and workflow state locks so a late completion continuation cannot overwrite a faulted terminal state.
- Made failed-build cleanup release output ports and report cleanup errors with `CleanupFailed`.
- Added public `Input` and `Output` properties to helper base classes while keeping the existing protected `InputBlock` and `OutputBlock` properties.
- Changed node fault behavior so queued diagnostic errors and final fault-hook diagnostics can still be observed.
- Added shared node disposal handling for sync-only and async-only runtime nodes.
- Faulted runtime graphs on startup failure and made the host release failed runtimes before returning the start result.
- Made startup fault cleanup best-effort so cleanup failures do not hide the original startup failure.

## Tests Added

- Reliable fanout delivers all values to fast and slow inputs.
- Disposing a link cancels a pending send to a full input.
- Host startup failure faults and disposes nodes that already started.
- Startup fault cleanup preserves the original startup failure even when a node throws during cleanup.
- Runtime async disposal disposes sync-only resource nodes.
- Build failure disposes async-only nodes already created by factories.
- Node fault keeps queued and final diagnostic errors observable.
- Registration helpers can use public node ports.
- Output port disposal completes a pump without source completion.
- Output port delivers a value buffered before link registration.
- Multi-source input completion link disposal does not fault the input.
- Runtime and host startup cancellation do not fault already-started nodes and leave state stopped.
- Dataflow-backed helper nodes run fault hooks before `Fault(...)` returns.
- Failed-start disposal errors do not replace the original start result and runtime is cleared.
- Sync and async runtime disposal keep cleaning remaining workflow nodes and resources after one node throws.
