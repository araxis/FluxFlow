# Direct fanout and ordinary control inputs

FlowDeliveryBatch had one remaining caller: FlowOutput.DeliverRoutesAsync.
The separate generic helper is removed. FlowOutput now calls its routes directly;
the generic target/state parameters, tuple state and injected send/fault delegates
are gone. No replacement class or interface is introduced.

Fanout still offers the message to every selected sibling before observing faults,
cancels blocked delivery when necessary, joins every started task and preserves
the original selection failure. Zero/single-route paths remain fast paths. Normal
multi-route completion returns Task.WhenAll directly, without adding a closure or
another async wrapper to the successful path. This is structural simplification;
no performance improvement was benchmarked or claimed.

The review of FlowControlInputs reaches a different conclusion. It is shared by
FlowNode and FlowSource for terminal inputs: independent cancellation, bounded
ActionBlocks, exception observation and joining those blocks after business
output drains. Deleting that helper alone would duplicate the required lifecycle
implementation. It remains unchanged; this step does not claim its removal.

Ordinary commands such as Gate.Open and Gate.Close do not need terminal semantics
or AddControlInput. NativeControlRegistrationTests demonstrates three actual
Dataflow ActionBlocks registered with HasInput, plus one native output. Open
allows a correlated reply; Close drops the next message; all registered inputs
remain ordinary inputs, and normal application shutdown completes successfully.
The Gate is a private test fixture, not a new production component/module.

Public API and package inventory are unchanged. FlowPorts remains deleted.
The remaining question is whether terminal-input behavior should remain in the
minimum Core or be extracted later; removing it now would change supported
shutdown behavior and is not implicit in removing unnecessary abstractions.

Validation evidence is recorded in `artifacts/direct-fanout-20260915/`. No commit,
push, package publication or external service change is included.

Final qualification: 2,387 tests passed in seven reports, zero failures/skips;
Release build passed with zero warnings/errors. Public API baseline and whitespace
checks passed. See `verification.json`, `final-tests/`, `final-tests.log` and
`build.log`. The earlier 2,385-test run predates the new ordinary-control fixture
and is not added to the final total. No new package consumer or performance run
was needed for this internal implementation change.
