# Application lifecycle and link cleanup

The application package keeps its existing responsibility: compose registered
components, attach native links, start sources and coordinate shutdown. This pass
removes indirection within those responsibilities without introducing new types.

## Changes

- A boolean records closed admission instead of a cancellation source whose token
  had no subscribers. Runtime lifecycle calls still synchronize on the existing gate.
- Stop and Fault share the existing lifetime coordinator, including before Start.
  Previously Fault before Start closed the application flag without faulting nodes.
  It now faults constructed components and retains the first failure. Fault before
  Build resolves no component factories.
- Link calls register directly with the application. Per-output keyed
  `Action<FlowLink>` callbacks and their registration helper are removed. Existing
  native port registration data still distinguishes application outputs from
  unrelated host block registrations. Native block identity rejects foreign ports.
- Business links retain their actual blocks during completion setup, eliminating
  per-edge keyed source resolution. Internal traversal avoids public workflow snapshots.
- Failed link attachment uses the normal cleanup path. Previously that failure path
  disposed links locally and again during application disposal; an unlink exception
  could also interrupt cleanup. All attached links now get one cleanup attempt, and
  startup and unlink errors remain observable together.

## Retained boundaries

Public signatures and configuration syntax are unchanged. DI owns component and
resource disposal. The application tracks successful construction only for cleanup,
not as another lookup registry. Native Dataflow delivery, fan-in completion, terminal
controls, event bridging and reentrant lifecycle task sharing remain intact.

## Regression coverage

Coverage includes fault before Start, first-failure retention, rejected admission,
fault before Build without construction, foreign providers with identical address
strings, rejection of undeclared host outputs and partial link attachment with both
successful and throwing unlink callbacks. Existing partial-build, fan-in, terminal
control, event and native-DI ownership tests remain.

Validation completed:

- Full Release solution: 2,039 passing test executions on .NET 8/10, no warnings.
- Debug solution: 32 projects built, no warnings or errors.
- Existing public API baseline test passes unchanged.
- Isolated Application and Configuration package consumers pass on .NET 8/10.
- `git diff --check` passes. No commit or publication in this step.

Local evidence: Release results in `%TEMP%/flux-application-simplification-full`;
package runs in `%TEMP%/fluxflow-application-consumer-cb930643efd04c71b7d1a935ff3d57a4`
and `%TEMP%/fluxflow-configuration-consumer-94e0c95c997b4bb09c6ad7e3158e21db`.
