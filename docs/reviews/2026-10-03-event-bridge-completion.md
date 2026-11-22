# Event bridge completion and failures

This pass implements item 5 of the Application review. ComponentEventBridge retains
its native TransformBlock and BroadcastBlock for address enrichment and event
publication. It introduces no production types or dependencies.

Completion is now the single task observed by every DisposeAsync call. Previously,
a second caller could return while the first still waited, because a separate
disposed flag skipped that wait. The flag and the retained source-link field are
removed. A private StopForwardingAsync method waits for component completion,
unlinks once, and completes the transform even when unlinking throws.

Completion waits for unlinking and both native blocks together. It reports all
independent failures instead of letting a forwarding failure hide an unlink failure.
Reference deduplication removes repeated exceptions caused by native downstream
and reverse fault propagation. One distinct exception retains its identity; multiple
failures are aggregated. Repeated disposal observes the same result or exception.

Component faults remain on component completion and do not fault the event stream.
The existing reverse output observer remains necessary to release transform results
when the broadcast output faults or completes prematurely. It is now included in
bridge completion rather than running unobserved. If Complete has already closed the
transform's input, Fault alone may leave queued results waiting for the failed
output. The observer attaches a native null target on this failure path to release
those results while preserving the output failure. Normal publication is unchanged.
Header snapshots,
message identity, ordered publication, late observation, unconsumed output handling
and native broadcast semantics remain unchanged. DI still owns bridge and component
disposal, with standard DI behavior when disposal fails.

New regression coverage exercises simultaneous forwarding/unlink failures, component
fault isolation, and multiple disposal callers before and after completion, on both
successful and failing cleanup paths. Existing event and application lifetime tests
cover normal draining, output faults, early output completion and attachment failures.
A regression test blocks an event destination, queues more messages than the output
capacity, completes the transform, then makes the destination fail. Completion must
settle with the original failure rather than wait indefinitely.

Validation: all 2,071 Release test executions pass on .NET 8/10 without warnings.
All 32 Debug projects build without warnings or errors. The isolated Application
package consumer passes on both frameworks. The source API baseline was refreshed
only for the internal bridge's DisposeAsync declaration/body style; its declaration
count and the exported public API remain unchanged. No commit or publication.
The final event/boundary suite also passes 25 consecutive runs (800 executions)
to exercise the shutdown race. `git diff --check` passes.

Local evidence: `%TEMP%/flux-event-bridge-cleanup-final`,
`%TEMP%/flux-event-bridge-race-final`, and
`%TEMP%/fluxflow-application-consumer-ecc7e95f2f8741ee9f0cccaa14db4355`.
