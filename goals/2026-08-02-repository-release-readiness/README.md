# GOAL: Close out the accumulated FluxFlow work as a release-ready repository state

## Status

- State: complete
- Date: 2026-08-02
- Repository: FluxFlow
- Branch: `work/major-surface-reset`
- Scope: accumulated-work audit, coherent local commits, isolated Release-test
  formatting, release-only real-provider validation, clean-worktree verification,
  maintainer documentation, goal evidence, and memory
- Compatibility posture: no new product feature and no intentional production
  behavior change in this closeout round

## Role And Execution Instruction

Act as a senior .NET library maintainer preparing a large, already approved body
of work for reliable review and eventual release. Treat every current tracked,
deleted, and untracked workspace change as authoritative user-owned work produced
by the preceding approved goals. Preserve it exactly unless this goal explicitly
authorizes a mechanical format correction, a release-validation workflow change,
or documentation/evidence updates.

This complete goal must exist on disk before staging, committing, formatting,
changing CI, or running clean-worktree verification. Execute it fully, then
update this file with exact commits, commands, results, limitations, and final
repository state.

Favor KISS, SRP, explicit ownership, direct commands, and small reversible
steps. Do not add runtime abstractions, reflection, scanning, dynamic code,
packages, generic repositories, background services, hidden registration,
test retries, arbitrary sleeps, or another product capability.

## Current Baseline

At goal creation:

- the branch is `work/major-surface-reset` at commit `1c69243d`;
- the worktree contains 398 status entries, including 332 untracked paths;
- the tracked diff spans 295 files with approximately 10,719 insertions and
  12,087 deletions;
- the accumulated changes cover the previously documented canonical-authoring,
  component, storage, durability, operational-status, retention, lease-renewal,
  instrumentation, sample, and release-verification goals;
- the most recent normal and serialized full suites each passed 2,488/2,488
  tests across 66 projects;
- the most recent serialized Release build passed 134 projects without warnings;
- the Release test project has a known baseline of 52 format findings outside
  the files touched by the last focused round; and
- the ordinary CI workflow restores, builds, and tests the solution, while the
  release workflow does not yet execute the explicit real networked-relational
  provider suites that intentionally live outside the main solution.

This baseline is evidence for closeout, not permission to rewrite unrelated
code or squash documented behavior into a different design.

## Objectives

Complete all agreed recommendations:

1. audit the accumulated worktree for accidental artifacts or sensitive files;
2. organize the approved accumulated changes into coherent local commits;
3. repair the known Release-test formatting findings as a separate mechanical
   commit, without mixing them into feature history;
4. make release validation execute both real networked-relational durability
   integration suites before packaging/publishing;
5. document the normal-versus-release validation boundary for maintainers;
6. validate the committed state from a clean detached worktree rather than
   relying on the long-lived dirty workspace;
7. run both explicit real-provider suites with their existing bounded container
   ownership and cleanup;
8. record exact verification and commit evidence in goal and memory; and
9. stop without selecting a speculative product feature.

## Safety And Scope Boundaries

- Do not discard, restore, overwrite, or reconstruct any current worktree file.
- Do not use `git reset --hard`, checkout-based file replacement, clean commands
  that remove untracked work, or broad deletion.
- Do not stage ignored build outputs, test results, database files, credentials,
  connection strings, logs, editor files, or temporary artifacts.
- Do not amend, rewrite, squash, rebase, or force-update existing commits.
- Do not push, open a pull request, merge, tag, publish, create a release, or
  trigger a remote workflow in this goal. Local commits and local clean-tree
  evidence are sufficient; remote collaboration remains a separate explicit act.
- Do not modify production behavior merely to make formatting or verification
  easier.
- Do not alter package versions, schemas, public API baselines, guarantees, or
  release notes except where an existing accumulated goal already did so.
- Do not add a new dependency, formatter, build tool, container library, or CI
  action.
- Do not add real-provider integration projects to `FluxFlow.sln`; ordinary
  development must remain server-free.
- Never print or persist generated database credentials or full connection
  strings.

## Pre-Commit Audit

Before staging anything:

1. enumerate tracked modifications, deletions, and untracked files;
2. inspect names for likely secrets, credentials, local databases, test results,
   coverage output, build output, editor state, or temporary files;
3. verify ignored files remain ignored and no generated `bin`/`obj` content is
   being considered;
4. run `git diff --check` for tracked content;
5. inspect `git diff --stat`, solution/project additions and removals, package
   inventory, public API baseline, documentation inventory, goal inventory, and
   memory inventory;
6. verify the untracked real-provider projects, samples, tests, docs, goals, and
   memory files correspond to the preceding accepted goals; and
7. stop rather than stage any suspicious file whose ownership cannot be tied to
   the documented work.

Line-ending conversion warnings caused by the repository's existing attributes
are not themselves defects. Do not normalize the whole repository merely to
silence those warnings. `git diff --check`, compiler/formatter results, and the
staged diff are the correctness gates.

## Commit Structure

Create four local commits. Before each commit, inspect the staged name/status,
staged diff summary, staged whitespace check, and representative staged diffs.
Use neutral, concise messages and do not mention assistant/tool/vendor names.

### Commit 1: accumulated approved implementation

Stage the complete authoritative worktree that predates this closeout goal,
excluding only files introduced specifically for the current readiness goal.
This commit intentionally records the already reviewed and repeatedly verified
canonical authoring, component cleanup, optional durability, provider,
operations, documentation, tests, goals, and memory as one internally consistent
repository state.

Use commit message:

`Complete canonical authoring and durable operations`

This is preferable to risky hunk-level historical reconstruction because many
shared solution, package, Engine, node, documentation-index, memory-index, and
release files were changed by multiple consecutive approved goals. The commit
must build as a whole and must not pretend those interdependent final-state edits
are independent.

Exclude from this commit:

- this readiness goal;
- the new readiness memory file;
- readiness-only documentation;
- readiness-only workflow changes; and
- formatting changes not already present at baseline.

### Commit 2: isolated formatting cleanup

After Commit 1, run the repository's existing formatter only against
`tests/FluxFlow.Release.Tests/FluxFlow.Release.Tests.csproj`. Allow it to change
only files in that project and only for analyzer/format corrections.

Inspect every resulting file. Reject semantic changes, renamed tests, changed
assertions, altered timeouts, skipped tests, new suppression, or broad files
outside the project. Re-run `--verify-no-changes` after correction.

Use commit message:

`Normalize release verification formatting`

If the formatter produces no changes because the baseline is already clean,
do not create an empty commit; record that fact and continue.

### Commit 3: release validation contract

Add the readiness-only workflow and maintainer documentation changes, including
this in-progress goal.

Use commit message:

`Strengthen release validation`

### Commit 4: factual completion evidence

After clean-worktree validation succeeds, update this goal to complete and add
the memory/current-state/progress evidence. Stage only those evidence files.

Use commit message:

`Record repository readiness evidence`

Do not create any commit while its required verification is failing or its
staged contents are not understood.

## Formatting Cleanup Requirements

Use the installed SDK's normal `dotnet format` command and the existing project
configuration. Do not add or change `.editorconfig`, analyzer packages,
`Directory.Build.props`, warnings, suppression files, or style severity merely
to make the command pass.

Required flow:

1. run the full Release-test project format check and capture the actual current
   findings;
2. run the formatter on that project only;
3. inspect the resulting diff for mechanical-only changes;
4. build the Release test project;
5. run the complete Release test project;
6. run format verification again; and
7. commit the format-only diff separately.

If any formatter change alters semantics or creates disproportionate churn,
revert only that formatter-produced hunk with an explicit patch and document the
reason. Never revert pre-existing user work.

## Release Workflow Improvement

Update `.github/workflows/publish-nuget.yml` so release validation runs both
explicit real networked-relational durability suites after the normal solution
test step and before pack/release/publish actions.

Use the existing project-owned runners:

- `tests/FluxFlow.Engine.DurableInput.TSql.IntegrationTests/run-integration.ps1`;
- `tests/FluxFlow.Engine.DurableOutput.TSql.IntegrationTests/run-integration.ps1`.

Each workflow step must:

- use `pwsh`;
- pass `-AcceptLicense` explicitly;
- rely on the runner's default isolated container name, random host port,
  generated ephemeral credential, bounded readiness, exact test execution, and
  `finally` cleanup;
- avoid duplicating image tags, credentials, connection strings, test filters,
  or container commands in YAML;
- fail the release before packaging when a suite fails, skips everything, or
  cannot start its owned server; and
- use clear neutral step names for durable-input and durable-output validation.

Do not add these suites to ordinary `ci.yml`. The ordinary pull-request/main
workflow must stay fast and server-free; full real-provider proof belongs to the
release path and explicit local maintainer command.

## Maintainer Documentation

Add `docs/38-release-validation.md` and list it in `docs/README.md`.

The document must explain:

- ordinary CI restores, builds, and tests `FluxFlow.sln` without external
  infrastructure;
- release validation additionally runs both explicit networked-relational
  durability suites before packaging;
- the integration projects intentionally remain outside the solution;
- exact local commands for the input and output runner;
- the required explicit license acknowledgement;
- external-connection mode for a CI-managed server, without showing a real
  connection string;
- bounded readiness, unique database/container ownership, no skipped-test
  acceptance, and guaranteed cleanup behavior;
- how to use `-KeepContainer` only for deliberate diagnostics and the resulting
  cleanup responsibility; and
- that a clean detached worktree is the preferred final local proof for a large
  accumulated change.

Do not duplicate full provider semantics or release instructions already owned
by other documents. Link to the relevant provider docs and integration project
README where useful.

If the input integration project lacks a README while the output project has
one, add a small symmetrical README for the input suite rather than forcing the
maintainer guide to carry provider-specific details.

## Clean Detached-Worktree Verification

After Commits 1 through 3, create a uniquely named temporary directory under the
system temporary root and add a detached Git worktree at the current `HEAD`.
Resolve and print the exact absolute temporary path before use. Never target the
repository root, user profile, or an unresolved variable.

Run verification only inside that detached worktree:

1. `dotnet --version` and repository SDK/platform inspection;
2. `dotnet restore FluxFlow.sln`;
3. serialized Release build with `ContinuousIntegrationBuild=true`;
4. complete normal Release solution test pass;
5. complete Release governance project;
6. format verification for the Release test project;
7. package vulnerability inspection under configured sources;
8. durable-input real-provider runner with `-AcceptLicense`;
9. durable-output real-provider runner with `-AcceptLicense`;
10. confirm neither runner leaves an owned container; and
11. `git status --short` in the detached worktree must be empty except for
    ignored build outputs.

Use bounded command timeouts and never overlap builds/tests or the two
container-owning suites. If the local container runtime is unavailable, record
the concrete failure and do not claim real-provider validation. Do not weaken or
skip integration tests to get a green result.

After verification, remove only the exact detached worktree through Git's
worktree removal command and prune its registration if needed. Confirm the
temporary directory and worktree registration are gone. Do not delete any
directory recursively with a broad or unresolved target.

## Final Evidence And Memory

Add `memory/289-repository-release-readiness.md` with:

- baseline counts and branch;
- audit results;
- exact four-commit structure or an explicit reason for any omitted empty
  formatting commit;
- formatting findings and corrections;
- release-workflow boundary;
- clean detached-worktree path and cleanup confirmation;
- exact build/test/integration/package results;
- documentation changes;
- remote actions deliberately not performed; and
- the next recommendation: choose a concrete product requirement before more
  refactoring.

Update:

- `memory/00-index.md`;
- `memory/01-current-state.md`;
- `memory/04-architecture-decisions.md`; and
- `memory/07-progress-log.md`.

When complete, update this goal with:

- `State: complete`;
- commit hashes and subjects;
- actual files changed for the readiness slice;
- exact verification counts and timings where available;
- format finding disposition;
- integration image/digest evidence without credentials;
- clean-worktree cleanup evidence;
- final branch/worktree status; and
- deliberate deferrals.

## Acceptance Criteria

The goal is complete only when:

- this full goal existed before staging or closeout changes;
- no suspicious artifact or secret is committed;
- all accumulated approved work is recorded in a coherent baseline commit;
- Release-test format findings are fixed mechanically in a separate commit, or
  a verified clean baseline makes that commit unnecessary;
- release validation runs both real-provider suites before any package/release
  action;
- ordinary CI remains server-free;
- maintainer documentation describes the exact validation boundary and commands;
- Commits 1 through 3 are validated from a detached clean worktree;
- clean restore, serialized CI build, full tests, Release governance, format,
  vulnerability, and both real-provider suites pass;
- both real-provider runners remove their owned containers;
- the detached worktree is removed safely after verification;
- final goal and memory evidence are committed separately;
- the primary worktree contains no unstaged or untracked repository changes;
- no push, PR, merge, tag, package publication, or remote workflow was performed;
  and
- no speculative product feature or production refactor was added.

## Deliberately Deferred

- Remote push, pull request, review, merge, tag, release, and publication.
- Any new workflow-engine capability.
- Any production refactor driven only by file size or aesthetics.
- Broader repository-wide formatting beyond the known Release-test project.
- Changes to ordinary CI that would require an external server on every pull
  request.

## Completion Evidence

Completed on 2026-08-02.

### Audit And Commit Results

- The initial audit confirmed 398 status entries, 332 untracked paths, and no
  suspicious generated artifact, local database, test result, editor state,
  credential, secret, or connection-string file.
- `git diff --check` passed after five new text files had one extra terminal
  blank line removed. Ignored build output was not staged.
- `3836baa9` — `Complete canonical authoring and durable operations` recorded
  the authoritative accumulated implementation.
- The planned `Normalize release verification formatting` commit was omitted:
  a fresh diagnostic verification reported zero files requiring formatting in
  the Release test project. The earlier 52-finding historical report did not
  reproduce, and no artificial or empty commit was created.
- `49a73115` — `Strengthen release validation` recorded the release workflow,
  documentation, input-runner README, and this executable goal.
- `0fb6e1b9` — `Stabilize sample output verification` recorded one test-only
  portability fix discovered by clean-checkout execution. It normalizes the
  expected raw literal and actual process output through the same existing
  helper while retaining the exact ten-line contract.
- `Record repository readiness evidence` is the final evidence-only commit
  containing this completed goal and memory updates. Its hash is obtained from
  `git log`; a commit cannot contain its own content-derived hash.

### Readiness Files

The readiness slice changed only:

- `.github/workflows/publish-nuget.yml`;
- `docs/38-release-validation.md` and `docs/README.md`;
- `tests/FluxFlow.Engine.DurableInput.TSql.IntegrationTests/README.md`;
- `tests/FluxFlow.Release.Tests/SampleDocumentationTests.cs`;
- this goal;
- `memory/289-repository-release-readiness.md`; and
- the memory index, current-state, architecture-decision, and progress files.

Ordinary `.github/workflows/ci.yml` was not changed. The release workflow now
runs the two existing project-owned real-provider runners after the normal
solution test and before packaging, using `pwsh` and explicit `-AcceptLicense`.

### Clean Detached-Worktree Verification

The authoritative successful checkout was
`C:\Users\meisa\AppData\Local\Temp\fluxflow-readiness-1785675548072` at
`0fb6e1b9`, using SDK 10.0.302.

- Restore: 134 projects, zero errors/warnings, 20.4 seconds wall time.
- Serialized Release CI build: 134 projects, zero errors/warnings, reported
  duration 1:38.44.
- Complete Release solution suite: 2,488/2,488 across 66 projects, zero
  warnings, reported duration 168.5 seconds.
- Complete Release governance project: 125/125, zero warnings, reported
  duration 57.8 seconds.
- Solution format verification: zero files requiring changes.
- Direct/transitive vulnerability inspection: no known vulnerable packages in
  any project under the configured sources.
- Real durable-input T-SQL runner: 89/89 passed, zero skipped, 5:58 reported.
- Real durable-output T-SQL runner: 117/117 passed, zero skipped, 7:40 reported.
- Both runners recorded image digest
  `mcr.microsoft.com/mssql/server@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`.
- No owned input or output test container remained.
- Detached-worktree `git status --short` was empty apart from ignored build
  output.

The first detached attempt exposed the line-ending-only assertion defect and
was intentionally abandoned. After the test-only correction, validation was
restarted from restore in a new clean checkout. The successful worktree was
removed through Git, its registration was pruned, and the exact path no longer
exists.

### Final Boundaries

No push, pull request, merge, tag, release, package publication, or remote
workflow was performed. No new product capability, production refactor,
runtime dependency, external-server requirement in ordinary CI, or speculative
next feature was added. The next round should begin from a concrete product or
operational requirement rather than another generic cleanup pass.
