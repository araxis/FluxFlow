# Release Test Determinism

Date: 2026-08-02

## Outcome

Release verification is deterministic, bounded, and build-explicit without
changing production behavior. The round remains entirely in test code,
documentation, goal evidence, and memory.

## Decisions

- Retry tests wait for an observable attempt gate and advance only the exact
  configured fake-time delay. They do not rely on scheduler yielding, sleeps,
  polling, or extra virtual-time movement.
- One small `ReleaseTestProcess` test helper owns child-process lifecycle. It
  starts commands without a shell, drains stdout and stderr concurrently,
  applies a finite timeout, keeps timeout distinct from caller cancellation,
  and terminates the owned process tree before returning from an abnormal path.
- Sample documentation tests select Debug or Release at compile time and run
  the matching prebuilt application with `--no-build --no-restore`. A missing
  build is a visible preparation failure.
- Existing release-script call shapes and normal nonzero-exit behavior remain
  intact. No runtime abstraction, dependency, reflection, background worker,
  package, or public surface was introduced.

## Regression Coverage

Focused tests prove normal and nonzero exit, exact large stdout/stderr capture,
timeout, caller cancellation identity, real descendant cleanup, timeout input
validation, environment removal/override, exact sample arguments, and causal
retry attempts. Assertions are externally observable and mutation-sensitive.

## Verification

- `FluxFlow.Resilience.Tests`: Release build clean; focused retry 1/1 plus ten
  repeated passes.
- `FluxFlow.Release.Tests`: Release build clean; process suite 5/5 across three
  observed runs; complete project 123/123.
- Three non-server samples: Release builds clean; serial prebuilt smoke 1/1.
- Solution: serialized Release build completed 133 projects/targets with zero
  warnings and errors.
- Full solution: two consecutive Release test passes, each 2,459/2,459 across
  66 projects with zero warnings.
- Both touched test projects passed format verification; the 14 documentation
  boundary tests passed after documentation updates; whitespace and owned-
  process audits were clean.

The source/test pairing analyzer reported 759 source files, 309 test files, 528
static pairs, and 231 unpaired source files. This is a discovery heuristic, not
coverage evidence, and no unrelated gap was expanded into this bounded round.

## Next Step

Keep the new boundary deliberately test-only. The next product round should be
chosen from production capability or operational evidence, not by generalizing
this helper into the runtime without a concrete need.
