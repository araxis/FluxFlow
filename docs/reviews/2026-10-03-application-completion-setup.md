# Application completion setup

This pass implements item 4 of the Application review. The old Initialize method
combined lifetime creation, native link attachment and completion coordination.
PrepareExecution now states the sequence directly, using private AttachLinks and
CoordinateCompletionAsync methods within the existing FlowApplication class.

GetPortCompletions shares the registered native block resolution used for input and
output completion. It returns distinct completion tasks, including unlinked ports
and named event outputs. Output aliases do not introduce additional completion work.
Producer dependencies continue to use the blocks already held by FlowLink. An
unnecessary copy of the component list is removed: composition is complete and Start
has already closed mutations before preparation runs.

Links attach before completion callbacks can close consumers and before sources start.
Fan-in still waits for every business producer. Terminal inputs are excluded from
business dependency cycles and remain open until output production ends. Partial
attachment failures use existing fault and cleanup handling. Validation still runs
at Build and Start because callers can add links between those phases.

No production type, persistent graph representation, registry or public API is added.
FlowLifetime and its concurrency/ownership behavior are unchanged.

New regression coverage checks that unlinked registered input/output ports delay
shutdown independently, that aliases remain valid, and that either direction's
unlinked port fault reaches the application and its components. Existing fan-in,
terminal-control, events and attachment-failure tests remain.

Validation: 2,063 Release test executions pass on .NET 8/10 with no warnings.
All 32 Debug projects build without warnings or errors. The public API baseline
passes unchanged. The isolated Application package consumer passes on both frameworks.
`git diff --check` passes. No commit or publication in this step.

Local evidence: `%TEMP%/flux-completion-setup-full` and
`%TEMP%/fluxflow-application-consumer-2004ca25bead470c8d307a1e8a97cc2c`.
