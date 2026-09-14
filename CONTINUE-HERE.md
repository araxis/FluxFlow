# Continue this project on another machine

## Active branch: framework-only component redesign

The complete implementation through step 79 is preserved on local `main` at
`6a659218`. The active `codex/core-only-component-redesign` branch intentionally
removes all 44 component packages and their extensions, component tests and old
samples. The 19 framework/infrastructure packages remain. Follow
[the new plan](docs/plans/2026-09-14-component-redesign.md); do not restore old
components or continue the historical component migration below automatically.

The framework-only checkpoint passed Release build (zero warnings/errors), all
3,070 local regression tests, and packaging of the 19 remaining packages.
Evidence: `artifacts/core-only-20260914/`. No remote push or publication occurred.

## Latest local checkpoint (2026-09-14)

Step 79 groups the message envelope, payload, error, content, JSON converters and
identifiers in `src/Core/FluxFlow.Core/Messaging/` (11 files moved unchanged).
The existing namespaces and APIs are retained; processing blocks stay in `Nodes/`.

The original transfer qualification below is historical. Subsequent authorized
refactoring has completed registration closeout (step 70): catalog/descriptor/activation forwarding and both
mutable registration builders are removed. See
[component loading ledger](docs/plans/2026-09-13-component-loading.md) and
[implementation ledger](docs/plans/2026-09-12-observability.md).
Fresh local package qualification and a consolidated local regression passed in
step 70. The subsequent Open/Close Gate component drops values while closed and
passed source validation (step 71); the package candidate predates Gate.
Step 72 adds direct context port registration: Gate uses AddComponent(type,
factory) and context.HasInput/HasOutput/HasEvents without ComponentContract.
Both runtime construction paths compile links against actual instances. Release
build is clean; the final local regression passed 4,640 tests in 76 reports under
artifacts/context-registration-20260913. The 44 older typed registrations remain
for a subsequent migration; their contracts are not yet removed globally.
No step-72 package qualification or publication occurred.
Step 73 adds async direct factories and context.SetLifetime for explicit completion
and additional owned-resource cleanup. Release build is clean; 4,646 local tests
passed in 76 reports under artifacts/async-context-registration-20260913.
This is migration preparation; the 44 typed registrations remain unchanged.
Step 74 migrates Routing: RoutingComponents and its three contracts are deleted;
typed AddWindow/AddCorrelation/AddJoin embed the same direct factories as AddRouting.
ApplicationSetup snapshots callbacks for Core and Engine, including revision change
detection. Release build is clean; 4,651 local tests passed in 76 reports under
artifacts/routing-context-migration-20260914. 41 legacy registrations remain.
No step-74 package qualification or publication occurred.
Step 75 migrates Assertions, Payloads and Validation: three more contract containers
are removed, retaining typed AddX methods and sharing context factories with DI.
No new framework API was needed. Release build is clean; 4,657 tests passed in
76 reports under artifacts/component-context-migration-20260914/regression-final.
38 legacy registrations remain. No new package qualification or publication.
Step 76 completes all remaining 38 builtin registrations. All 45 registrations
across 19 families now use direct context factories; 44 typed AddX methods and 55
capture overloads remain. The remaining 15 contract containers are removed.
Release build is clean; 4,688 local tests passed in 76 reports. Fresh candidate
0.0.0-local.20260914.context.1 contains 63 packages; independent package-only
consumers passed on net8.0/net10.0 and comprehensive package acceptance on net8.0.
Evidence is under artifacts/all-context-migration-20260914. See
[builtin migration report](docs/reviews/2026-09-14-builtin-context-registration-complete.md).
There are zero builtin registrations left to migrate. Legacy custom-template APIs
in Composition and custom samples/tests remain: audit those for the next cleanup,
then remove obsolete layers while preserving needed behavior. No publication or
external consumer adoption occurred. Earlier counts and pending migrations above
are step-specific history, superseded by step 76.
Step 77 completes the remaining custom/API cleanup. ComponentContract and its
helper layers, contract maps and the empty bootstrap are removed. Samples,
benchmarks, package fixtures and current documentation use direct context
factories. Final local regression: 4,627 passed, zero failed/skipped, 76 reports.
Fresh 63-package candidate 0.0.0-local.20260914.cleanup.2 passed all-family consumers
and comprehensive SQLFile restart acceptance on both net8.0 and net10.0.
The reduced test count reflects obsolete API-only cases removed/replaced; the
report describes coverage and the separate unfiltered prerequisite failures.
See [registration cleanup report](docs/reviews/2026-09-14-registration-cleanup.md).
No internal registration migration remains. Earlier pending cleanup statements
are historical and superseded. Next separate boundary: FluxMQ-NG adoption and
release/version preparation; neither was performed.
Step 78 completes the subsequent authorized FlowValue refactoring. FlowValue is
abstract with lazy Json/CreateJson; JsonFlowValue holds dynamic JSON, and the
materializer/helper APIs are removed. HTTP/MQTT requests and FlowError inherit the
base. Canonical FlowMessage stores one payload; IsError and persisted envelope
compatibility remain. Capture conversion errors are contained as error messages;
actual store failures remain fail-closed. Final local regression: 4,630 passed,
zero failed/skipped, 76 reports. Final candidate 0.0.0-local.20260914.value.2 has 63
packages and passed independent/generated-metadata consumers and comprehensive
SQLFile restart acceptance on net8.0/net10.0. See
[lazy value report](docs/reviews/2026-09-14-lazy-flow-value.md) and
artifacts/lazy-flowvalue-20260914/verification.json. Earlier API descriptions and
qualification counts are step-specific history. No internal work from this
refactoring remains; external adoption/publication is still separate.
Do not reset this modified tree to the original clean transfer checkpoint.

## Subsequent working-tree changes

After verifying the transfer checkpoint below, the user authorized the
Authoring-to-Definition rename and extraction of all editor/UI code into the
independent `FluxFlow.UI` package consumer, now located outside this repository at
`C:\Projects\FluxFlow.UI`. See
[UI extraction and current validation](docs/reviews/2026-09-12-ui-extraction.md).
These changes are not committed yet. The clean-transfer instructions below
describe the original checkpoint, not this modified working tree; preserve the
current work rather than resetting it to satisfy that historical check.

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
