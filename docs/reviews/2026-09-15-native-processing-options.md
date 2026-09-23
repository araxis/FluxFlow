# Native Dataflow processing options

FlowNodeOptions is removed. FlowNode, FlowTransformNode and CallFunction now take
ExecutionDataflowBlockOptions directly and a separate outputCapacity integer.
Developers configure the actual Dataflow block rather than learning a second
options object. The transform's separate ensureOrdered argument is removed;
use the native EnsureOrdered property. No replacement options class is added.

Migration: InputCapacity becomes BoundedCapacity; OutputCapacity becomes the
constructor's outputCapacity argument. MaxDegreeOfParallelism stays on the native
options. Omitted options preserve bounded input 128 and serial execution. Explicit
options retain native defaults, including unbounded input if no capacity is set.
Output remains bounded at 128 by default and must have positive capacity. The old
Clock property had no execution consumer and is removed rather than replaced.

Native cancellation also signals the shared processing Stopping token and faults
the owned outputs, so active cooperative work and pending output delivery can
terminate. Cancellation remains a lifecycle failure, not an ordinary flow error.
Component work must observe Stopping; cancellation cannot forcibly interrupt it.
The registration is disposed with the component. Ordinary processing exceptions
still become error messages and subsequent messages continue.

Tests cover cancellation before construction and during active processing for
both processing bases, native scheduler use and unordered concurrent transforms.
Existing ordering, backpressure and shutdown tests remain. Five obsolete options
validation/default cases per framework were removed; six behavior cases per
framework were added (net increase: two executions). Output-capacity rejection
tests now exercise constructors directly. The package-only Increment fixture uses
native bounded parallel execution with capacity-two output.

This is a breaking preview API change. Three base classes still remain; common
base simplification is pending. No commit, push or publication was performed.

Qualification: 2,375 tests passed in seven reports, zero failed/skipped. Release
build passed with zero warnings/errors. Fresh package-only consumers passed on
net8.0/net10.0. Public API baseline and whitespace checks passed. Evidence:
artifacts/native-options-20260915/verification.json, qualified-tests/,
build-final.log and consumer.log. No benchmark or external service run was needed.
