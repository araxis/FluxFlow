# vNext Stable Port Runtime

Date: 2026-07-17

## Status

The fourth bounded vNext milestone is implemented on local branch
`work/stable-port-runtime-vnext`. No push, tag, publication, pull request, or
merge was performed.

This milestone adds canonical stable ports and direct runtime interaction. It
does not replace the established Engine definition runtime, add final system
events or diagnostics, register ports in DI, implement provider snapshots or
runtime revisions, migrate component families, or redesign MQTT.

## Runtime Surface

- Added the additive `FluxFlow.Engine.Ports` namespace over canonical
  `FluxFlow.Composition.Addressing.ApplicationAddress` values and
  `FluxFlow.Nodes.FlowMessage<T>` envelopes.
- `ApplicationPortRuntimeBuilder` registers exact typed inputs and outputs with
  finite capacities and rejects resource addresses, reserved system inputs,
  duplicate addresses, and invalid capacities.
- `ApplicationPortRuntime` exposes stable port metadata, compiled-link
  activation, revision attachment, terminal completion, and direct
  `SendAsync`, `ReceiveAsync`, `ObserveAsync`, and `SendAndReceiveAsync` APIs.
- Address direction and payload-type mistakes are programming errors. Full,
  unavailable, completed, and timeout states are explicit result values;
  caller cancellation remains cancellation.

## Stable Inputs

- Each input owns a bounded FIFO mailbox independent of the current component
  target. Intake rejects immediately when unavailable, full, or completed.
- Input replacement pauses only that dispatcher, waits for an already claimed
  target offer to finish, swaps the target, and resumes queued mailbox work on
  the new revision.
- Generation-safe leases prevent stale revision cleanup from detaching a newer
  target. If a target rejects or throws while accepting a claimed message, the
  message is retained and retried when another target attaches.
- Runtime completion stops new intake. Output hubs drain before stable inputs
  receive their terminal signal, so already queued output fan-out is not
  rejected merely because completion began.

## Stable Outputs And Direct Access

- Each output owns a bounded ingress and broadcasts every message separately to
  compiled workflow links, one-shot receivers, and bounded observations.
- Component source completion or fault detaches only that source and never
  completes the stable output or a shared downstream input. Multiple source
  attachments may overlap while revisions drain.
- Compiled conditions receive ordinal `input`, `payload`, and `message`
  variables. A false condition affects only its link; a condition exception or
  full, unavailable, or rejecting target cannot stop sibling delivery.
- `ReceiveAsync` and `ObserveAsync` are broadcast taps and never consume data
  away from workflow links. Slow observation overflow faults/removes only that
  observation.
- `SendAndReceiveAsync` installs a response waiter before sending and selects
  the first response with the request `TraceId`.
- Added a bounded best-effort `Rejections` stream for input status, condition,
  target, source, and observation failures. It is a precursor to, not a
  substitute for, the next system-event and diagnostics milestone.

## Compatibility And Versioning

- `FluxFlow.Engine` moves from local `2.0.3` to additive `2.1.0`.
- The legacy `FluxFlow.Engine.Definitions` and `FluxFlow.Engine.Runtime`
  behavior remains unchanged. Engine's duplicate definition model still waits
  for the planned major-version migration and legacy reader.
- Engine now references Composition and Nodes for the canonical address, link,
  and envelope contracts. Port factories are explicit generic delegates; no
  assembly scanning or reflection-based discovery was introduced.
- The reviewed public source-declaration baseline changes only for Engine, from
  407 to 503 declarations.

## Verification

- Engine tests: 77 passed, including 14 stable-port tests.
- Composition tests: 116 passed.
- Composition.Hosting tests: 17 passed.
- Release convention tests: 93 passed.
- Complete Release solution test sweep: every project passed with zero failures
  and zero skips.
- Controlled Debug build: 0 warnings and 0 errors.
- Controlled Release build: 0 warnings and 0 errors. The first pass timed out
  late without compiler errors; one incremental rerun found a stale lock on a
  Routing composition intermediate. After SDK build-server shutdown and a
  focused successful rebuild, the unchanged full rerun passed.
- Release preflight passed for alias `engine` version `2.1.0`.
- The public feed contains Engine `2.0.2`, while immediate local predecessor
  `2.0.3` is unpublished. A historical `2.0.3` package was built from commit
  `620aaa3` into a temporary source outside the repository. Binary package
  compatibility passed against it through the helper's file-URI source form.
- A temporary source containing Data `1.0.0`, Mapping `1.0.3`, Nodes `2.0.0`,
  Composition `2.1.0`, Engine `2.0.3`, and Engine `2.1.0` passed archive and
  symbol inspection, net8 consumer restore/build/run, and feed-style
  verification.
- A separate net8 package consumer compiled and executed the new typed builder,
  target/source attachments, direct send, and direct receive APIs and printed
  `STABLE_PORT_API_OK`.

## Next Gate

Implement isolated runtime status plus bounded `System.Events.Output` and
best-effort `System.Diagnostics.Output` streams. Preserve the stable-port
contracts, make system events workflow-linkable through canonical output
addresses, and integrate diagnostics with standard .NET logging, activity,
metrics, and diagnostic-source surfaces. Do not combine that pass with DI
provider snapshots, transactional revisions, component migration, or MQTT.
