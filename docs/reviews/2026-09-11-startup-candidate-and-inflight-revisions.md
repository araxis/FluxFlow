# Startup candidate and in-flight revisions

## Current local candidate

Version: `0.0.0-local.20260911.startup.1`.

Local feed: `artifacts/core-refactoring/local-candidate-20260911-startup/feed`.
Machine-readable acceptance: the sibling `acceptance.json`.

This candidate includes the internal source-start transaction repair described in
2026-09-11-candidate-startup-transaction.md. It replaces resource.2 as the current
local test candidate; prior feeds remain unchanged. Nothing is published.

- All 81 package archives and 81 symbol archives have coherent internal dependency
  versions. All 162 target DLLs, symbols and 81 packaged READMEs match the qualified
  build/source. All-package consumers load the same 162 target DLLs.
- Ten all-package consumer commands passed across .NET 8 and .NET 10: restore,
  build, behavior checks and separate-process durable seed/recovery on each.
- Twelve minimal-consumer commands passed: Core-only and Fluent-only restore/build/
  execution on both frameworks, with exact dependency closure checks.
- The candidate links to the unchanged source checkpoint with 4,848 selected local
  passes across 81 reports and 5,058 discovered cases. Those tests were not rerun
  merely to inflate this turn's counts. A fresh full nonincremental solution build
  and consumer build are clean, and one additional authored acceptance test passed.
- The 1,488 qualification inputs remain unchanged. Relative to resource.2, only
  the three internal Engine files, two test files and reviewed declaration baseline
  differ. The binary inventory records 16 changed target DLLs across eight Engine
  and dependent packages; Core target DLLs are unchanged.

The first all-package restore was denied access to the existing user configuration.
That failed attempt is retained. A separately authorized restore used new isolated
workspace caches and passed; no user configuration was edited.

## In-flight revision proof

The package-only console case `Revision_changes_preserve_inflight_requests` ran
four times, twice per framework, reversing framework order for the second pair.
Each run exercises 32 compatible revisions: 16 applied, eight startup rejections
and eight caller cancellations. Each attempt holds one accepted request in the old
node, holds candidate startup, then queues sixteen additional requests.

For successful updates, all new-node replies arrive while the old accepted request
is still held. Apply remains incomplete and the old node is not disposed until that
request is released and answered. On rejection/cancellation, the application and
port revision remain unchanged and all queued requests use the original node.

Final totals: 128 update attempts and 2,176 replies. Every request has the expected
revision/value and shared trace, with a distinct exchange correlation within its
run. Exact processing sets reject loss/duplication. All 264 node/source instances
were disposed once; all request tasks settled, live port queues were empty before
stop, and the captured port-completion task completed successfully after stop.

Fixture, runtime-file and report hashes were verified. Evidence lives under
`artifacts/core-refactoring/inflight-revisions-20260911/qualified`; assertion and
reasoned mutation review is in `.testagent/inflight-revisions-review.md`.
Earlier fixture-teardown mistakes and an assembly-lock rebuild failure remain
documented separately and are not counted as successful runs.

## Local consumer handoff

A consumer can add the absolute local feed directory as a package source and select
`0.0.0-local.20260911.startup.1` consistently for its FluxFlow dependencies. The
isolated fixtures demonstrate that no project references are required. The startup
repair introduces no exported API change relative to resource.2. The larger
refactor still has the previously documented breaking migration requirements.
No external consumer task has been contacted or changed.

## Limits and remaining gates

These are controlled correctness/stress runs, not measured sustained throughput or
latency. They cover compatible port surfaces, small string payloads and no installed
telemetry listener. They do not prove private registry size, incompatible surface
updates, arbitrary postcommit failure recovery, external broker/provider behavior
or long-retention stability. The selected local regression exclusions remain in its
recorded command. Full feature migration proof, remaining representative performance/
telemetry/provider gates and final deliverables still require completion before
the overall refactor or a public release can be called finished.
