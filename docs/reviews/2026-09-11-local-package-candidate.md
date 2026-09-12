# Local package candidate qualification

Historical candidate. Current-source qualification is recorded in
`2026-09-11-local-package-refresh.md` for `0.0.0-local.20260911.t1400`.
The evidence below remains specific to the older candidate.

Candidate: `0.0.0-local.20260911.t0930`. Local only; nothing published and no
external consumer coordination performed.

Evidence directory: `artifacts/core-refactoring/local-candidate-20260911-0930`.
The `feed` subdirectory contains all 81 manifest packages and their 81 symbol
archives. `inventory.json` records every archive hash, internal dependency,
target-framework assembly hash and exact pack arguments.

## Package provenance

Packages were created without rebuilding from the Release assemblies qualified by
the timer-burst checkpoint: 4,409 local regression passes and 4,619 discovered cases.
Both supported frameworks are present in every archive. All internal package
dependencies use the same local candidate version. Package metadata is overridden
for local consumption; existing qualified assembly identities remain unchanged.
Source hashes, not the checkout commit alone, identify this dirty-worktree candidate.

The first pilot version had an invalid leading-zero numeric prerelease segment and
was rejected. The valid version above uses `t0930`. The successful pilot is separate
from the final feed.

## Consumer verification

`consume.ps1 -Attempt consumers-qualified` creates a fresh project/cache for each
framework, copies the repository acceptance fixture and mechanically adds all 81
candidate references. Source mapping restricts internal packages to the local feed;
external dependencies use the public source. Asset validation rejects project
references, unexpected internal coordinates and non-isolated caches. Each restored
internal archive must hash-match the candidate inventory.

Both net8.0 and net10.0 restore and build cleanly and pass every existing marker:

- JSON execution, unchanged definition and rejected-update retention.
- Code-first conditional link, resource-dependent exact output and health readiness.
- Fluent source/transform/sink completion and exact output.
- Local durable input/output storage behavior.
- Separate-process seed and recovery, expired leases, captured workflow output,
  pending output resumption and exact idempotent effects.

Ten consumer commands exit zero. `consumers-qualified-verification.json` records
both results and 1,481 stable source inputs. This is package compatibility and
representative behavior, not runtime coverage of all 81 modules.

## Fixture migration and retained failures

The first net8 consumer restored all 81 and built cleanly, then failed because its
observed terminal output had no business route. Receive observation and durable
capture are intentionally not business routes. The fixture now explicitly declares
`DiscardUnmatched` on its JSON terminal output, code-first terminal output and
restart-captured terminal output. The conditional upstream link remains strict;
all prior behavioral assertions remain. No production implementation changed.

Exactly two source inputs differ from the packed snapshot: the two acceptance
fixture C# files. The original failed consumer, cache and logs remain under
`consumers` and the original log names. A subsequent sandbox attempt failed to read
the user package configuration; its artifacts remain under `consumers-terminal-policy`.
The successful fresh attempt ran with approved access under `consumers-qualified`.

## Follow-up validation and limits

`verify-fixture.ps1` performs a nonincremental full Release build, the release check
suite and solution discovery. `validate.ps1` verifies their outcomes, checks the
source migration scope and compares every rebuilt package DLL to its frozen archive
inventory. All checks passed: zero build warnings/errors, 252 release checks passed
with zero failed/skipped, 4,619 discovered cases, and all 162 rebuilt package DLLs
matching the frozen inventory. `final-verification.json` records these results.

Do not publish this local-only candidate or infer complete release readiness.
Remaining gates include performance tradeoff acceptance, telemetry/context and
broader integration/provider qualification, and the final capability/handoff audit.
The external application has not tested this candidate.
