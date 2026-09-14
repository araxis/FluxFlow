# Registration closeout and Gate

## Registration checkpoint

Step 70 closed the builder removal with fresh local packages and a consolidated
regression. Candidate `0.0.0-local.20260913.registration.2` contains 63 packages.
The independent net8.0 package consumer passed configuration, code-first/direct
construction, resources, health, durability and process-restart recovery. The
consumer was outside this repository and restored into its own package cache.
Archive hashes and target assembly inventories are in
`artifacts/registration-closeout-20260913/verification.json`.

Validation also found and fixed a missing fixture namespace import and a real
RequestReply shutdown race. The initiating fault must reach the native blocks before
cancellation can trigger a cleanup fault. A deterministic reentrant timer test
failed before the ordering correction and passes after it. The complete pre-Gate
local regression passed 4,610 tests in 76 reports.

This candidate predates Gate. It is a local verification candidate, not a published
release or external consumer adoption. No external broker/database suites were run.
The second pack disabled NuGet vulnerability lookup after the first lookup failed;
package execution evidence is not security-audit evidence.

## Gate: concrete component-development exercise

`GateNode` is in the existing Routing runtime package. It has Input, Output, Events,
Open and Close. It starts closed unless `initiallyOpen` is true. Closed-state values
are discarded; open-state values retain their original FlowMessage. Existing errors
continue through Output, so closing a gate does not hide upstream failures.
No JSON conversion, internal payload type, timeout or retained-message queue is needed.

`Open` and `Close` ignore payloads and update an atomic state. Input observes state
when processing begins. Concurrent inputs have no cross-port ordering guarantee;
SendAsync acknowledges admission rather than completion of the control action.
Already-emitted output is never retracted by Close.

The component reuses FlowNode for processing and error forwarding. The concrete gap
exposed by this exercise was terminal controls on processing nodes. FlowNode now
reuses the existing FlowControlInputs implementation, also used by sources. It waits
for output drain before joining controls and cancels them on fault. Nodes with no
controls retain their existing completion task. Gate contains no tracing, metrics,
logging or shutdown machinery of its own.

The optional adapter registers one CreateBound factory with five fixed port bindings.
It binds two local options and constructs GateNode; there is no Gate descriptor,
registration builder, options builder or new project. Ordinary C# constructs the
node directly. `AddRouting()` supplies `flow.gate` for flat JSON configuration.
The existing 44 typed definitions remain; Gate is the 45th direct registration.

Coverage includes open/close/drop behavior, envelope identity, error forwarding,
capacity validation, bounded delivery and fault release, native controls, input
completion, feedback to Close in a real FlowApplication, hosted configuration, and
joining/canceling active controls. Routing READMEs show C# and JSON usage. Family
README ownership language and corresponding conventions now say contracts instead
of removed descriptors/catalogs.

Final Gate verification is recorded in step 71 of both implementation ledgers.
Sessions, Constants and revision hosting remain separate deferred topics.
