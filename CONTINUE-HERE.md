# Continue this project on another machine

## Start here

Open this repository folder directly in a new session. Do not create another
worktree or depend on the previous machine's workspace. Copy the **whole FluxFlow
folder, including hidden `.git`, `artifacts`, `.testagent`, and `graphify-out`**.
The branch contains source and documentation; ignored local qualification data
and package feeds travel with the folder, not with a normal Git clone.

Transfer branch: `work/core-refactoring-transfer-20260912`.
Canonical folder on the previous machine: `C:\Projects\FluxFlow`.
The folder may have a different absolute path on the new machine.

First read this file completely, then run the read-only transfer check:

```powershell
pwsh -NoProfile -File eng/verify-machine-transfer.ps1
```

This verifies the standalone repository, branch/commit, qualified source hashes,
local candidate packages and evidence hashes without restoring or rebuilding.
For every transferred file, including build outputs and historical evidence:

```powershell
pwsh -NoProfile -File eng/verify-machine-transfer.ps1 -Full
```

The machine-local transfer manifest and recovery notes are in
`artifacts/transfer/manifest.json` and `artifacts/transfer/state.json`.
Run this check **before** editing, restoring, building, or cleaning: build outputs
and historical qualification inputs are intentionally covered by the manifest.
Do not try to make old test reports pass by rewriting their paths or hashes.

## Current result

The approved deep simplification goal is complete **locally**. This is not a
published release and not external consumer acceptance.

- Canonical instance-first construction: application → workflows → component
  instances. Components can be constructed normally or through their own static
  factory. No general component creator is required for ordinary use.
- Core owns canonical messages/values, native bounded delivery, lifetime and
  request/reply. Optional authoring, configuration, update hosting, editor and
  operational capabilities stay outside ordinary Core execution.
- Preserve simplicity, SRP, SOLID and explicit dependencies. Do not add universal
  managers, parallel lifetime/transport algorithms, custom subscriptions, central
  type switches or backward-compatibility wrappers without a concrete requirement.
- Reports/dashboards and backend telemetry configuration are consumer-owned.
- Context-aware Add, templates and feedback/iteration remain explicitly deferred.

The final real-service run discovered a source-event/control shutdown dependency
cycle. The final correction orders source production and full/control completion
separately using the existing dependency sorter. Business writers drain before
controls close; event outputs drain before their linked readers close. The first
partial fix was rejected by the existing file-watch test and was corrected.
Core implementation and both Core binaries were unchanged by that correction.

## Verified checkpoint

Candidate: **`0.0.0-local.20260912.integrated.1`**.

Relative feed: `artifacts/core-refactoring/local-candidate-20260912-integrated/feed`.
Do not use an older candidate merely because an older report mentions it.

| Check | Result and scope |
| --- | --- |
| Full Release rebuild | Zero warnings and errors |
| Configured local regression | 4,868 passed; 76 nonempty suites, 81 reports, five explicitly excluded opt-in suites |
| Real database input/output | 90 + 117 passed on net10.0 |
| Real broker-to-HTTP workflow | One passed, including shutdown, on net10.0 |
| In-process acceptance | One passed, separate from the real-service and regression counts |
| Local packages | 81 packages, 81 symbol archives, 81 READMEs, 162 target DLLs verified |
| Fresh package-only consumers | All 36 commands passed on net8.0 and net10.0 |
| Final source/service identity | 1,492 stable source inputs; 218 stable test/runtime DLLs; 42 production copies matched the packages |

Focused/repeated tests overlap those totals; do not inflate unique test counts.
Real-service conformance was source-suite execution, **not** package-only or
net8.0 server conformance. Separately configured external-endpoint smoke is unrun.
Disposable containers were removed. The database container license was confirmed
for the previous machine's run; do not assume a service is configured here.

Primary reading, in order:

1. [Integrated candidate and final fix](docs/reviews/2026-09-12-integrated-local-candidate.md).
2. [Consolidated architecture and acceptance handoff](docs/reviews/2026-09-11-refactoring-handoff.md).
3. [Final requirement reconciliation](docs/reviews/2026-09-12-final-acceptance-reconciliation.md).
4. [Execution ledger](docs/plans/2026-09-08-core-refactoring-progress.md).
5. [Migration guide](docs/46-code-first-major-migration.md) and
   [component coverage ledger](docs/17-component-coverage-matrix.md).

Machine-readable evidence:

- `artifacts/core-refactoring/local-candidate-20260912-integrated/acceptance.json`
- `artifacts/core-refactoring/real-integration-20260912/final-complete/verification.json`
- `artifacts/core-refactoring/real-integration-20260912/service-verification.json`
- `.testagent/event-drain-review.md`

Previous evidence and package feeds remain immutable. Historical manifests and
scripts contain absolute paths from the former workspace; these are provenance,
not a requirement to recreate that path. The transfer verifier uses relative
paths. Copy a historical runner into a **new** attempt and adapt that copy if a
fresh run is needed; never overwrite frozen reports or repack the same version.
Historical nested checkouts are copied as plain archival directories, without
their secondary Git metadata. The root repository remains independent.
The graph index is historical and includes removed types; it is not evidence for
the current architecture. Its cached interpreter path is machine-specific.

## Performance interpretation

This engine targets regular applications, not competitive actor-framework or
gaming workloads. No numerical application SLO was supplied. Representative
performance, bounded-state recovery and retirement checks have been completed.
Their reports retain their original build identities and limitations; do not
present them as freshly measured on this machine or on the last shutdown patch.
Measured regressions and telemetry overhead were documented, not waived or called
speedups. Do not reopen a generic exhaustive benchmark matrix without a concrete
new requirement or changed behavior.

## What is not done, and the next decision

1. **External consumer adoption is not done.** The intended consumer is
   **FluxMQ-NG**, not the original FluxMQ. With explicit authorization, point that
   consumer at the relative integrated.1 feed, adopt the breaking API changes and
   run its real workflow/dashboard scenarios. Do not infer permission to message
   or edit another task/project simply from this continuation prompt.
2. **Publication is not done.** No new package release, tag or main-branch merge
   was part of qualification. This transfer branch is a source checkpoint, not a
   release. Consumer validation and a deliberate release/version decision come
   before publishing. Do not push additional changes or publish without a request.
3. **New-machine validation is not done.** First verify transfer integrity. Then,
   if requested, check installed SDK/runtimes and run a fresh, separately named
   verification attempt. `global.json` allows feature-band roll-forward; the
   qualified machine used SDK 10.0.400 and runtimes 8.0.30/10.0.11. Do not install
   runtimes, accept licenses, start external services or change system settings
   merely to recover historical status.

The old main checkout had unrelated uncommitted work. It was preserved locally
before checkout of this branch, in the named stash and full working-folder backup
recorded in `artifacts/transfer/state.json`. Do not apply that stash onto this
branch: it would reintroduce an older overlapping state. Inspect or restore it
separately if the user requests it. Copying the whole folder also preserves this
recovery material and repository history.

## Ready-to-paste continuation prompt

> Continue this FluxFlow project from the folder I opened, directly in this
> checkout rather than a new worktree. Read CONTINUE-HERE.md completely and follow
> its reading order. Run eng/verify-machine-transfer.ps1 before modifying anything.
> Inspect the branch, commit, working-tree status and transfer manifest. Summarize
> the verified current state, any actual transfer problems, and what remains
> unperformed. Treat the local simplification goal as completed; do not restart
> it or reopen completed checks without evidence. Preserve frozen package feeds,
> reports and the pre-transfer recovery state. Tell me the next concrete decision
> for FluxMQ-NG adoption and release preparation, then wait for my direction.
> Respond in English. Do not publish, push, merge, install dependencies, alter
> system configuration or coordinate another project merely because this session
> is a continuation.
