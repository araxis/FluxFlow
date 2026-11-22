# Core native completion and error forwarding

This pass is confined to Core; Application composition and Configuration are unchanged.

## Request/reply

PendingRequests now returns Task<FlowMessage>. Its existing native completion source
carries replies and exceptions directly, replacing a nullable reply/error tuple.
FlowRequestReply awaits that task without inspecting a custom result or manually
rethrowing an exception. No new outcome class, task wrapper or coordination layer.

Original exception objects, including cancellation tokens and aggregate exceptions,
remain intact. Public RequestAsync still reports cancellation as a canceled task.
The pending task carries an OperationCanceledException as an exception to preserve
its identity; the public async boundary supplies the ordinary cancellation outcome.

The single timer, capacity/deadline rules, first-result arbitration, asynchronous
continuations and cooperative admission cleanup remain. Failed scheduling during Add
observes the newly faulted task that cannot be returned, while draining existing waiters.

## Processing

Removed the internal EmitsDirectly flag. FlowTransformNode returns an error through
its ordinary transform result; FlowNode overrides that existing forwarding operation
to emit through its own direct output path. The base no longer inspects the subtype's
emission mode. The override is private protected and sealed in FlowNode, so it adds
no component-author requirement or external extension point.

Forwarded errors retain message identity. Processing exceptions still create derived
error messages with causation. Sequential success/error ordering is preserved for
both authoring styles, including a bounded native output.

## Review boundary

The source and processing lifetimes retain their different shutdown responsibilities.
Their synchronization protects actual processing, output and control races. They were
not merged into a generic lifecycle abstraction, and no synchronization was removed
merely because registration is synchronous. Message/value and expression contracts
remain unchanged. Further Core simplification should be based on specific duplication
or unnecessary responsibility, rather than removing useful behavior or combining
different lifecycles.

## Verification

Full Release suite: 1,989 passing executions, zero failures or warnings, with the
existing .NET 8/.NET 10 target matrix. The isolated Core package consumer passes
both targets. TRX evidence is in the temporary flux-core-native-completion-final
directory. The isolated consumer evidence directory is
fluxflow-core-consumer-6fde82ac14bc40a5b360b786a6b7557d.

Added regressions for send/stop exception identity and task status, Add-time timer
failure draining, and mixed success/forwarded-error/processing-error ordering.
The source-declaration baseline includes public members of internal classes, so its
PendingRequests task signature was refreshed; the externally accessible API is unchanged.
No commit, push or publication.
