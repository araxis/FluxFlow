# Repository Release Readiness

Date: 2026-08-02

## Outcome

The accumulated canonical-authoring and optional-durability work is now stored
as coherent local commits on `work/major-surface-reset`, release validation owns
both explicit real T-SQL provider gates, and the committed result passed a
complete clean detached-worktree verification. No runtime capability, public
API, schema, package version, or ordinary-CI infrastructure requirement was
added in this closeout round.

## Baseline And Audit

The round began at `1c69243d` with 398 status entries, 332 untracked paths, and
a tracked diff across 295 files (10,719 insertions and 12,087 deletions). The
untracked set was checked for build output, test results, coverage, databases,
editor state, logs, credentials, secrets, and connection strings. No suspicious
artifact was found, ignored output remained excluded, and `git diff --check`
passed after five newly created text files had their extra terminal blank line
removed. The accumulated files mapped to the previously accepted goals.

## Commit Organization

- `3836baa9` — `Complete canonical authoring and durable operations`: records
  the authoritative accumulated implementation, tests, documentation, goals,
  and memory as one internally consistent baseline.
- The planned format-only commit was omitted. A fresh diagnostic
  `dotnet format` verification of the Release test project reported zero files
  requiring changes, so creating an empty or artificial commit would have been
  misleading. The 52 findings recorded by the preceding historical run did not
  reproduce on the committed baseline.
- `49a73115` — `Strengthen release validation`: adds the release-only provider
  gates, maintainer documentation, the input-runner README, and the executable
  closeout goal.
- `0fb6e1b9` — `Stabilize sample output verification`: applies one test-only
  portability correction discovered by the first clean checkout. The expected
  raw literal contained checkout-native CRLF while captured output was already
  normalized to LF. Passing both through the existing normalizer preserves the
  exact ten-line assertion on every checkout and platform.
- `Record repository readiness evidence` — the commit containing this memory
  record, the completed goal, and the four memory-index/state updates. Its hash
  is intentionally obtained from `git log` because a commit cannot contain its
  own content-derived hash.

## Release Validation Boundary

Ordinary `.github/workflows/ci.yml` remains server-free and continues to
restore, build, and test `FluxFlow.sln`. The publication workflow now runs the
existing durable-input and durable-output T-SQL integration runners, each with
explicit license acceptance, after the normal solution test and before packing
or publishing. The provider projects remain outside the solution.

The YAML does not duplicate server tags, credentials, connection strings,
readiness logic, test filters, or cleanup commands. Each project-owned runner
retains isolated naming, random-port allocation, ephemeral credentials, bounded
readiness, no-skip enforcement, image-digest reporting, and `finally` cleanup.

`docs/38-release-validation.md` documents the normal/release boundary, exact
local commands, external-connection mode, diagnostic container retention, and
clean-worktree procedure. The durable-input integration project now has the
same focused operator README boundary as durable output.

## Clean Detached-Worktree Evidence

The authoritative successful run used
`C:\Users\meisa\AppData\Local\Temp\fluxflow-readiness-1785675548072` at
`0fb6e1b9`. The first detached attempt correctly exposed the line-ending defect
above; validation was stopped, the defect was fixed in the primary worktree,
and the entire sequence was restarted from a newly created clean checkout.

- SDK: 10.0.302.
- Restore: 134 projects, zero errors and zero warnings (20.4 seconds wall time).
- Serialized Release CI build: 134 projects, zero errors and zero warnings;
  reported build time 1:38.44.
- Complete Release solution test: 2,488/2,488 passed across 66 projects, zero
  warnings; reported test time 168.5 seconds.
- Complete Release governance project: 125/125 passed, zero warnings; reported
  test time 57.8 seconds.
- Solution-wide format verification: zero files requiring changes.
- Direct and transitive package vulnerability audit: no known vulnerable
  packages under the configured sources for every project.
- Durable input T-SQL integration: 89/89 passed, zero skipped; reported duration
  5:58.
- Durable output T-SQL integration: 117/117 passed, zero skipped; reported
  duration 7:40.
- Both integrations tested
  `mcr.microsoft.com/mssql/server@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`.
- Neither runner left an owned container, and detached-worktree Git status was
  clean apart from ignored build output.

The detached worktree was removed through Git, pruned, and its exact temporary
path no longer exists. No credential or full connection string was printed or
persisted.

## Deliberate Limits And Next Recommendation

No push, pull request, merge, tag, package publication, release, or remote
workflow was performed. No speculative workflow-engine feature or aesthetic
production refactor was selected.

The next round should start only from a concrete product or operational
requirement. The repository does not currently need another generic cleanup
round; review, remote collaboration, or a specifically justified capability is
the appropriate next decision.

## Remote Candidate Follow-up

Draft pull request 65 published the complete branch after this local closeout.
Its first ordinary CI run, 30750160166, restored and built successfully but
failed one Release-governance fact on the Linux runner. The fact passed on
Windows because `Path.GetFileNameWithoutExtension` accepts backslashes there;
on Linux, project-reference values written with backslashes remained complete
relative paths instead of becoming project names.

The focused correction keeps the change in test infrastructure. One private
helper normalizes backslashes to portable separators before extracting the
filename, and an exact two-row theory covers both backslash and forward-slash
project-reference forms. The existing real project-boundary fact continues to
exercise the helper. Focused Release verification passed all three discovered
cases with zero warnings, file-scoped formatting required no change, and no
production code, dependency, workflow, timeout, skip, or public contract was
modified. The replacement remote run remains the authoritative Linux proof.
