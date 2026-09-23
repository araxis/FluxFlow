# Application lifetime comparison

A single regression scenario now constructs the same JSON definition and factory
registrations through direct Core and Engine update hosting. Both execute a
transform in one workflow and two sinks in another. The first operation is held
open while two more messages are admitted, then stopping begins before the
operation is released. The second operation throws; the third succeeds.

Both paths must drain all three accepted messages before stop completes. Both
branches receive the same envelope instances, including the error envelope; the
error retains the failing input's trace ID. Components complete successfully and
are disposed exactly once. No replacement component package was added: the nodes
are private test fixtures.

The test also records a real lifetime difference. Engine Stop retires its active
revision and disposes its components. Direct FlowApplication Stop drains its
graph, while Dispose releases component ownership. The test explicitly checks
these different disposal counts immediately after Stop, then checks exactly-once
disposal after Dispose. Replacing one lifetime with the other would be an
observable change; shared execution must not accidentally conflate them.

This is behavioral characterization, not a production refactor or evidence that
all runtime behavior is equivalent. Request/reply, transactional rollback,
source/control shutdown, dependency resources and external persistence retain
their existing separate tests; this new scenario does not establish cross-runtime
equivalence for those features. No production code, public API or package identity
changed. Counts remain 11 production and 29 solution projects.

Validation: the full Engine suite on net10.0, including both new scenarios, is
recorded in artifacts/lifetime-parity-20260915/engine.log and its TRX report.
No new package consumer or external service test was needed for this test-only
step. Next: separate shared execution completion from ownership release when
consolidating Core/Engine; preserve explicit graph disposal and revision retirement.
