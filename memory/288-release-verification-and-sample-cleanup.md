# Release Verification And Sample Cleanup

Date: 2026-08-02

## Outcome

Release verification now serializes only test classes that own child processes,
while unrelated xUnit collections remain parallel. The durability operations
sample uses one causal telemetry-completion signal instead of four without
changing its behavior or ten-line output.

This round changed no production source, public API, dependency, package,
schema, provider, dispatcher, workflow definition, JSON contract, registration
surface, or delivery guarantee.

## Process-Test Decision

Eleven release-test classes launch child processes through the shared process
helper or script runner. They now belong to one explicit
`ReleaseProcessCollection`. Normal xUnit collection behavior serializes those
classes with each other; the collection deliberately does not set
`DisableParallelization`, so file-only governance tests remain parallelizable.

No global runner setting, static semaphore, retry attribute, sleep, or semantic
timeout increase was added. `ReleaseTestProcess` remains the only test-owned
process lifecycle boundary and still distinguishes timeout from caller
cancellation, preserves cancellation-token identity, drains stdout/stderr, and
kills the owned process tree.

The blocking fixture continues to resolve scripts with `$PSScriptRoot`, but its
child starts in the stable system temporary root rather than the unique script
directory deleted by fixture disposal. A live process therefore does not hold
the fixture-owned directory as its current working directory. The real
descendant PID assertion remains unchanged.

## Sample Simplification

`DurabilityTelemetry` now has one thread-safe observation dictionary, one fixed
list containing the required metric and activity keys, and one completion
source. Both listeners record into that bounded map and complete the signal only
after the full semantic set exists.

`Program.cs` waits on two actual scenario boundaries: delivery-handler
completion and telemetry-set completion. The single scenario cancellation
token still bounds the wait. There is no polling, arbitrary delay, gauge,
hosted status reader, telemetry framework, reflection, or new dependency.

The exact sample output is unchanged. It still proves pre-start pending input,
the transformed value, input/output metrics and activities, delivered/completed
terminal status, and exactly three explicit status reads with automatic polling
off.

## Test Quality

The exact-output fact remains the behavioral authority and still performs two
sequential process runs, checks both exit codes and stderr values, freezes the
complete first output, and compares the second normalized output to the first.

The source-shape fact retains the unobservable architecture protections:
host-owned and filtered listeners, listener disposal, normal host lifecycle,
provider-neutral status stores, exactly three status calls, source-generated
JSON, exact temporary-directory cleanup, one Hosting package, and absence of
polling/server/reflection/synchronous-blocking constructs. Redundant assertions
against private filtering fragments, query type tokens, loop spelling, and the
same forbidden token in both source and project text were removed.

Pseudo-mutation review found the important mutations killed by the retained
tests: removing process-tree termination or cancellation identity fails the
lifecycle facts; changing output, status transitions, signal names, or ordering
fails the exact-output fact; removing status calls, listener filtering/disposal,
generated JSON, or cleanup fails the source-shape fact. Assertion review found
no new assertion-free, trivial-only, or self-referential test.

## Documentation

The sample README, `docs/05-hosting-and-observability.md`, and
`docs/35-durability-operational-status.md` were reviewed. They already describe
host-owned listeners, causal completion, explicit status snapshots, no
background polling, provider neutrality, at-least-once behavior, and disposal
accurately. Because no public behavior changed, the documentation site was not
churned solely to create a diff.

## Verification

- Mandatory Roslyn pairing analyzer, exactly once: 766 source files, 313 test
  files, 531 statically paired, 235 unpaired, 3,361 ms. This is a static
  heuristic, not line/branch coverage.
- Release test build: 45 projects, zero errors/warnings.
- `ReleaseTestProcessTests`: 5/5 passed.
- Timeout/cancellation lifecycle pair: five consecutive runs, each 2/2 passed.
- Operations sample build: nine projects, zero errors/warnings.
- Direct sample: two identical successful executions with the exact ten lines.
- Durability operations facts: 2/2 passed.
- Complete `SampleDocumentationTests`: 6/6 passed.
- Complete Release project with normal parallel settings: two consecutive
  passes, each 125/125 with zero warnings.
- Touched sample and Release C# format gates: passed with no changes. The 52
  whole-project findings recorded by the preceding round remain pre-existing
  and were not bulk-rewritten.
- Serialized Release solution build: 134 projects, zero errors/warnings.
- Normal full Release suite: 2,488/2,488 across 66 projects, zero warnings.
- Serialized full Release suite: 2,488/2,488 across 66 projects, zero warnings.
- Final combined documentation-boundary and sample-documentation filter:
  20/20 passed with zero warnings.
- Final process-owner audit: the same eleven launcher classes and eleven shared
  collection attributes, with no `DisableParallelization` override.
- Final `git diff --check`: passed.

## Next Step

Stop cleanup driven only by code size. The process/test and sample boundaries
are now small and explicit. Choose the next round from a concrete product or
operational requirement; do not split the cohesive production durability state
machines without evidence that doing so improves correctness or ownership.
