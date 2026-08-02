# Durable Output Capture Foundation

Date: 2026-07-30

## Objective

Add the smallest honest provider-neutral foundation for a future durable
outbox. Selected application outputs must be stored before ordinary Engine
dispatch while unselected outputs keep the existing bounded in-process path.
The round must remain explicit, reflection-free, provider-free, and small.

## Final Boundary

`FluxFlow.Engine` now owns two narrow extension contracts:

- `IApplicationOutputCaptureResolver.Resolve<T>(ApplicationAddress)` selects an
  optional typed capture once when an output port is built.
- `IApplicationOutputCapture<T>.CaptureAsync(...)` completes before the port
  dispatches the original message to revision routes, regular links, receive
  waiters, or observations.

`ApplicationOutputPort<T>` still owns one bounded ingress and one serial pump.
It awaits configured capture while holding the existing dispatch gate and then
runs the unchanged synchronous fan-out. No second queue, middleware pipeline,
hosted service, timer, scope, or background owner was added. Revision drain
already waits for the dispatch gate, so it also waits for in-flight capture.

`ApplicationPortRejectionReason.OutputCaptureFailed` reports serialization,
conflict, and store failures without dispatching uncaptured data. Abort passes
the port lifecycle token to capture. A successfully committed record is not
retracted if later live dispatch is interrupted.

## Provider-Neutral Package

`FluxFlow.Engine.DurableOutput` 1.0.0 contains only the capture foundation:

- `DurableOutputKey`: canonical workflow output address plus existing
  `MessageId`.
- `DurableOutputEnvelope`: immutable value/error JSON, stable contract and
  schema identity, original message/trace/correlation/causation metadata,
  message and capture timestamps, and defensively copied headers.
- `IDurableOutputStore.EnqueueAsync(...)`: one atomic idempotent store method.
- `DurableOutputEnqueueStatus`: `Enqueued`, equivalent-content
  `AlreadyExists`, or different-content `Conflict`.
- `DurableOutputRegistrationBuilder`: one flat explicit `Capture<T>(...)`
  declaration containing the address, stable contract name, and
  `JsonTypeInfo<T>`.
- `AddFluxFlowDurableOutput(...)`: one registration callback, one immutable
  configuration snapshot, exactly one store, and one capture resolver.

Equivalent duplicate declarations and repeated service registration are
idempotent. Address, contract, payload-type, store-count, and resolver conflicts
fail before partial descriptors are appended. System/resource addresses are
not accepted by the durable adapter.

Serialization is explicit and source-generation friendly. The package performs
no assembly scanning, reflection discovery, dynamic activation, per-message
service resolution, or default store selection.

## Guarantee And Limitation

For a configured output, Engine dispatch starts only after the store returns
`Enqueued` or `AlreadyExists`. `Conflict`, serialization failure, a mismatched
store result, or a store exception prevents dispatch and faults the output.
The existing bounded ingress supplies ordering and backpressure.

The producing component may transfer a message into the Engine's in-memory
output ingress before the durable commit. This round therefore does not claim:

- producer/business-state atomic acknowledgement;
- external delivery;
- exactly-once execution;
- workflow completion acknowledgement; or
- durable workflow/component state.

`ApplicationPorts.ReceiveAsync(...)` and `ObserveAsync(...)` remain live taps.
They see selected outputs only after capture, but they are not persistence
contracts and are not used to implement capture.

## Dependency And Complexity Result

Dependency direction remains:

```text
FluxFlow.Engine <- FluxFlow.Engine.DurableOutput <- future providers
```

Engine references no durable package or provider. The new package references
Engine and the already-used DI abstractions only. It has no SQL, hosting,
logging, MQTT, HTTP, resilience, or transport dependency. No setting was added
to `FluxFlowApplicationOptions` and no new third-party package version was
introduced.

## Test Evidence

The mandatory bounded testing workflow added 38 methods / 45 cases with 209
assertions and completed an assertion-quality and pseudo-mutation review with no
remaining gap.

Behavior evidence includes:

- unconfigured bypass;
- capture before revision routes, regular links, `ReceiveAsync`, and
  `ObserveAsync`;
- `Enqueued` and `AlreadyExists` dispatch;
- conflict, serialization, store, and result-key failures without dispatch;
- exact serial ordering and bounded backpressure;
- deterministic `TimeProvider` envelope metadata;
- in-flight drain and abort behavior;
- immutable envelope validation;
- flat registration, idempotency, and conflict atomicity; and
- no runtime service locator or provider dependency.

Focused results:

- `FluxFlow.Engine.Tests`: 97 passed, zero warnings.
- `FluxFlow.Engine.DurableOutput.Tests`: 37 passed, zero warnings.
- Engine and DurableOutput formatting verification passed.

Repository results:

- serialized Debug build: 127 projects, zero errors/warnings;
- serialized Release build: 127 projects, zero errors/warnings;
- serialized Release suite: 1,770 tests in 61 test projects, zero warnings;
- package/manifest/documentation/public-API focused gates: 22 passed;
- public source-declaration baseline reviewed, accepted, and reverified;
- `FluxFlow.Engine.DurableOutput.1.0.0` binary and symbol packages created;
- direct archive inspection found the README and both net8.0/net10.0 binaries;
- the repository archive-inspection script itself was not executed because the
  machine blocks PowerShell script files; no policy bypass was used.

## Documentation

Updated the repository README, changelog, package README, public API overview,
runtime architecture, Engine README, durability roadmaps, docs index, package
manifest, public API baseline, current-state memory, progress log, and this
indexed record. `docs/27-durable-output-capture.md` is the complete user and
provider contract.

## Deliberately Deferred

- SQL-file durable-output provider and schema.
- Delivery leasing, retry scheduling, and dead-letter operations.
- MQTT/HTTP/other transport adapters.
- Retention, replay, CLI, UI, and administration.
- Producer/business-state transaction integration.
- Workflow completion acknowledgement and checkpoints.

The next recommended round is the SQL-file durable-output provider. It should
implement only atomic idempotent enqueue first, keep provider configuration in
its own flat builder, and avoid adding delivery operations to
`IDurableOutputStore`.
