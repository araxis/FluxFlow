# Synchronous construction: preparation for keyed DI

The user approved one ordinary application provider, native keyed singleton component
and port lookup, synchronous construction, and DI-owned disposal after application drain.

This step removes the asynchronous component factory overload. Production Scheduler,
Gate, Source and Mapper registrations already constructed synchronously. Async examples
in tests and package consumers exercised infrastructure rather than a component requirement.
Those examples now use synchronous factories. Startup remains asynchronous.

BuildApplicationAsync still awaits failed-build cleanup. Removing async construction does
not make disposal or source startup synchronous, and does not yet migrate ownership.

Release build: zero warnings/errors. Full regression: 2,649 passed, zero failed/skipped,
29 TRX. Four fewer executions reflect removing the duplicate sync/async variants of two
tests across both frameworks. Lazy creation, duplicate-address rejection, and failure
cleanup coverage remain. Public source baseline updated for the removed overload.
Evidence: artifacts/synchronous-construction-20260920/.
Fresh package-only Core and Configuration consumers passed on both .NET 8 and .NET 10,
including revision construction and the configured Scheduler-to-mapper pipeline.

## Not completed

The broader requested migration remains: native keyed component/port singleton resolution,
removing Workflow's port dictionaries and the temporary lookup provider, and coordinating
DI disposal with Application stop/drain. Existing revision providers also require review;
their runtime update behavior must not be silently removed. No new registry or context
was added in this step. No commit, push or publication was performed.
