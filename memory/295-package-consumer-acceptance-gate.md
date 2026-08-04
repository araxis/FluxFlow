# Package-Consumer Acceptance Gate

Date: 2026-08-04

## Outcome

FluxFlow now retains the representative external-consumer execution proof that
was previously performed only during the coordinated release train. Normal CI
and complete package rehearsals can restore package artifacts into a fresh
consumer, verify the exact candidate bytes, compile against public APIs, and
execute the canonical Engine, Fluent DSL, and local durability paths.

## Small Boundary

- `eng/package-consumer-acceptance/` is a checked-in `net8.0` console consumer.
  It has four package references, versioned through runner-supplied properties,
  and no project reference, test framework, repository path, reflection, or
  generated application code.
- `eng/package-consumer-acceptance.ps1` owns orchestration. It names the exact
  nine-package FluxFlow dependency closure, resolves identity and version from
  the existing manifest/project files, and either consumes a completed
  rehearsal source or packs that closure from an already completed Release
  build.
- Restore occurs in a new external work directory with `--no-cache`, a local
  package root, and a temporary source configuration containing `<clear/>`, the
  candidate source first, and the public source for external dependencies.
- The runner rejects project libraries and requires the restored FluxFlow
  coordinates to equal the explicit closure. Each cached FluxFlow `.nupkg`
  must have the same SHA-256 value as its candidate-source archive.
- The existing per-package consumer smoke remains unchanged. It provides broad
  restore/load coverage; the new gate provides narrow representative behavior.

## Consumer Scenarios

1. Strict canonical JSON is deserialized, registered through ordinary DI,
   started through `FluxFlowApplication`, exercised through stable input/output
   ports, and stopped.
2. A consumer-owned typed source/transform/sink graph runs through the public
   Fluent DSL and produces the exact expected value.
3. Provider-neutral durable input and output envelopes are persisted through
   the SQL-file providers, the first container is disposed, a new container
   reopens the files, idempotent duplicate results are required, and both exact
   envelopes are leased from persisted state.

Each scenario and the overall executable emit an exact success marker. The
runner requires every marker exactly once.

## Automation

Normal CI invokes the runner with `-PackPackages` after the complete solution
tests. A complete manifest rehearsal invokes the same runner once with its
already-populated candidate source after all archive inspections and
per-package dry runs succeed. The individual publication workflow remains
single-package and does not pretend to own a complete candidate closure.

## Verification

- The real runner passed under Windows PowerShell and PowerShell 7. Both runs
  packed and hash-verified the exact nine-package closure and emitted the
  Engine, Fluent, durability, final, and completion markers.
- The 12 focused package-consumer contracts passed, and the complete release
  test project passed 163 tests with zero warnings.
- The controlled Release build passed 134 projects with zero warnings and zero
  errors; the complete solution passed 2,531 tests across 66 projects with
  zero warnings.
- Solution formatting, standalone-consumer whitespace, and the full transitive
  vulnerable-package audit passed.
- The first remote run found a test-only portability assumption: styled Linux
  error rendering wrapped the phrase between `candidate` and `archive`. A
  Linux PowerShell reproduction confirmed that the runner correctly rejected
  the altered archive. The contract now asserts the stable fragment while
  retaining the non-zero, single-restore, and ownership checks.
- Exact-head CI run `30925479707` passed restore, build, all solution tests,
  and the new Linux package-consumer acceptance step on commit
  `6372051316a7eda3617999ece00c248b187cc8ee`.
- Pull request 75 had no review findings and merged normally as
  `014840dd6c35a6f3e74d8bc104ca78ceb7b62d74`. Local `main` was synchronized
  cleanly to the same commit before final evidence closeout.

## Boundaries

This round adds release/CI validation, a package-only fixture, focused
governance tests, documentation, goal records, and memory only. It does not
change runtime source, public APIs, public API baselines, package projects,
dependencies, schemas, package versions, changelog entries, tags, repository
releases, or public package state.
