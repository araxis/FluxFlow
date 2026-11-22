# Application startup and failure handling

This pass covers the first two Application review items: lifecycle readability and
consistent reporting of initiating and cleanup failures.

Build and Start now call one private cleanup method. The caller rethrows its original
exception when cleanup has no independent failure. When cleanup fails independently,
the original exception stays first and additional failures are deduplicated by
reference. Comparing aggregate leaves avoids reporting the same startup failure twice
when shutdown observes its inner exceptions. Stop shares the combination rule.
Dispose continues to attempt every link cleanup and wait for event bridges; the
provider remains solely responsible for disposing components and resources.

Start checks successful composition before publishing its shared startup task. A
premature call, including one from a component factory, produces a clear error and
does not prevent later legitimate construction or startup. The synchronization gate
and mutation check are private because no other type uses them. Existing shared task
publication stays in place for lifecycle callbacks that reenter the application.

The existing lifetime coordinator now checks stop requests before each source starts
and observes failure after each awaited source startup. This closes two races: later
sources starting after Stop, and successful Start being reported after a fault during
the final source's startup. There is no additional state machine or production type.

Regression tests cover premature and factory-reentrant Start, aggregate startup
exception identity, independent and repeated build cleanup errors, Stop during startup
and faults during final-source startup. Existing lifecycle, partial construction,
native DI ownership, fan-in and terminal-control tests remain.

The full run exposed a hosting fan-out test with capacity two consumers and a
three-message sample. Native broadcast does not guarantee delivery to a full
consumer. The test now gives each sink capacity for its entire sample, retaining
its checks for startup ordering, message identity and completion. Production
delivery semantics and default test capacities elsewhere are unchanged.

Validation: 2,049 Release test executions pass on .NET 8/10 with no warnings;
32 Debug projects build with no warnings or errors. The public API baseline passes
unchanged. The isolated Application package consumer passes on both frameworks.
Release evidence: `%TEMP%/flux-lifecycle-failures-final`. Package evidence:
`%TEMP%/fluxflow-application-consumer-3ae0358b59ce446f876c6488b7bc1126`.
No commit or publication in this step.
