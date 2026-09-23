# Configuration factory parity — 2026-09-19

Configuration had retained a narrower factory check than Core: it recognized only
ComponentFactory, rejecting registrations using the already-supported native async
Func overload before Build could invoke them. New tests reproduced this failure on
both net8.0 and net10.0 before the production change.

Configuration now recognizes both existing contracts. It still only registers
descriptors: node construction, awaiting asynchronous factories, cancellation between
factories and failure cleanup remain in BuildApplicationAsync. No new construction
path, production class or public API was introduced.

Removed a redundant pre-registration check for an existing component factory. Existing
workflow registration already rejects that duplicate workflow, and AddComponentFactory
guards duplicate component factories. The required post-registration check remains,
now including both supported forms. Invalid registration still leaves host services
unchanged through the existing staging mechanism.

This step fixes contract drift and removes duplicated validation; it does not delete
another class or claim a large structural reduction.

## Validation

- Release solution build: zero warnings/errors.
- Full regression: 2,553 passed, zero failed/skipped, 29 TRX reports.
- Eight additional executed cases across the two target frameworks cover deferred
  construction, independent builds, async failure cleanup and missing-factory rollback.
- Public declaration baseline unchanged and passed in the full suite.
- Fresh Configuration/Scheduler package-only consumer passed on net8.0/net10.0 using
  candidate `0.0.0-local.20260919215308`. It now composes a synchronous Scheduler and
  asynchronous sink, including owned-resource resolution and disposal ordering.
- Separate Core/Routing consumers were not repeated in this step.

Evidence: `artifacts/configuration-factory-parity/`. `before.log` retains the expected
pre-fix async registration failures. No commit, push or publication occurred.
