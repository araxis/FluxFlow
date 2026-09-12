# Continue this project on another machine

## Start here

Open this repository folder directly in a new session. Do not create another
worktree or depend on the previous machine's workspace. Copy the **FluxFlow
folder, including hidden `.git`**. This is a lightweight source checkpoint:
generated artifacts, package feeds, build outputs and cached analysis were
intentionally removed to reduce transfer size. Source, documentation, Git history
and the recovery stash are retained. A restore/build is required before execution.

Transfer branch: `work/core-refactoring-transfer-20260912`.
Canonical folder on the previous machine: `C:\Projects\FluxFlow`.
The folder may have a different absolute path on the new machine.

First read this file completely, then run the read-only transfer check:

```powershell
pwsh -NoProfile -File eng/verify-machine-transfer.ps1
```

This checks the standalone repository, transfer branch, working-tree cleanliness
and Git object connectivity without restoring or rebuilding. It does not verify
removed binaries or independently repeat the historical qualification.
Run it **before** editing, restoring or building. Do not try to recreate historical
test results by rewriting report paths or hashes.

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

The former local feed was
`artifacts/core-refactoring/local-candidate-20260912-integrated/feed`.
It is **not included** in this lightweight copy. The candidate and results below
describe the previous qualification, not files currently available in this folder.
Create a new uniquely versioned candidate and fresh evidence when validation is
authorized; do not claim that newly built packages are the same qualified bytes.

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

Historical machine-readable evidence paths (not included in this copy):

- `artifacts/core-refactoring/local-candidate-20260912-integrated/acceptance.json`
- `artifacts/core-refactoring/real-integration-20260912/final-complete/verification.json`
- `artifacts/core-refactoring/real-integration-20260912/service-verification.json`
- `.testagent/event-drain-review.md`

Detailed findings remain in the tracked documents above. The original workspace
on the previous machine still holds the raw evidence and feeds as a recovery copy;
it is not a runtime or development dependency. Historical document paths are
provenance, not a requirement to recreate that workspace. Generate fresh outputs
under a new attempt/version when requested. The graph cache and historical nested
checkouts were removed. Two old machine-specific indexing hooks are preserved but
disabled in `.git/hooks`; configure indexing afresh only if needed.

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
   **FluxMQ-NG**, not the original FluxMQ. With explicit authorization,
   first build and qualify a fresh local package feed, then point that consumer
   at it, adopt the breaking API changes and run its real workflow/dashboard
   scenarios. Do not infer permission to message
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
before checkout of this branch. The Git recovery stash is
`14f1a9ba36036398bdc35f2dc610fbf24283b387`, labelled
`Pre-transfer main checkout 2026-09-12`; it travels with `.git`.
Generated outputs and the extra working-folder backup were moved outside the project to
`C:\Projects\FluxFlow-transfer-recovery-20260912` on the previous machine.
The old checkout backup is inside `artifacts/transfer/before-20260912` there.
This is optional recovery material, not needed to continue this checkpoint.
Do not apply the stash onto this branch: it would reintroduce an older overlapping
state. Inspect or restore it separately if the user requests it.

## Ready-to-paste continuation prompt

> Continue this FluxFlow project from the folder I opened, directly in this
> checkout rather than a new worktree. Read CONTINUE-HERE.md completely and follow
> its reading order. Run eng/verify-machine-transfer.ps1 before modifying anything.
> Inspect the branch, commit, working-tree status and Git integrity. Summarize
> the verified current state, any actual transfer problems, and what remains
> unperformed. Treat the local simplification goal as completed; do not restart
> it or reopen completed checks without evidence. This is a source-only transfer:
> generated feeds and raw reports were intentionally removed; their absence is
> expected, not corruption. Preserve the pre-transfer recovery stash. Tell me the
> next concrete decision
> for FluxMQ-NG adoption and release preparation, then wait for my direction.
> Respond in English. Do not publish, push, merge, install dependencies, alter
> system configuration or coordinate another project merely because this session
> is a continuation.
