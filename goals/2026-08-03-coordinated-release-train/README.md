# Goal: Coordinate, Harden, And Execute The Canonical Release Train

## Status

- State: in progress
- Date: 2026-08-03
- Repository: `C:\Projects\FluxFlow`
- Base branch at declaration: `main`
- Accepted base commit: `e113f504d61bb818e5c005a3d1b552b7baa3a1f9`
- Working branch: `work/release-train-safety`
- Scope: release collision audit, dependency-safe release planning, bounded
  release-workflow hardening, focused governance tests, complete package
  rehearsal, normal review and merge, package publication, public-feed
  verification, and durable evidence
- Runtime feature scope: none

## Objective

Publish the validated canonical FluxFlow package graph without reopening runtime
design or weakening any existing gate. Keep the release boundary explicit,
fail-closed, dependency-aware, recoverable, and small enough for an operator to
understand in one sitting.

The completed work must:

1. audit every manifest package's intended id/version, remote tag, repository
   release, and public package-feed state before mutation;
2. distinguish a legitimately reusable published prerequisite from a version
   collision instead of hiding either behind duplicate skipping;
3. compute deterministic publication waves from explicit package-project
   references rather than assuming `eng/packages.json` is topologically sorted;
4. prevent the release workflow from publishing a public repository release
   before the corresponding package is accepted and restorable from the public
   feed;
5. remove duplicate-skipping from the publication command so a version race or
   collision is visible;
6. preserve the existing solution, real-provider, archive, consumer, notes, and
   indexing checks;
7. merge release-safety changes normally before creating new tags;
8. publish only from one exact clean merged commit, in dependency-safe bounded
   waves;
9. stop the train immediately on any failed run, unexpected existing version,
   wrong tag target, skipped provider test, indexing failure, or consumer
   failure; and
10. verify the completed graph from the public feed, then update documentation,
    memory, and this goal with exact evidence.

## Accepted Baseline

The preceding merge-validation goal established the following release-candidate
evidence before this goal began:

- `main` was clean and synchronized;
- the serialized Release build passed for 134 project targets with no warning
  or error;
- the complete solution passed 2,495/2,495 tests across 66 projects;
- Release governance passed 127/127 tests;
- formatting and direct/transitive vulnerability gates passed;
- the real durable-input provider passed 89/89 tests with zero skip;
- the real durable-output provider passed 117/117 tests with zero skip;
- all 59 manifest packages passed preflight, prepare-only tag resolution,
  package and symbol creation, isolated-cache consumer loading, archive
  inspection, and local-feed verification; and
- no tag, release, package publication, or public-feed mutation was performed.

This goal may change release tooling, tests, operator documentation, goal
records, and memory. It must not change runtime behavior, public runtime APIs,
schemas, provider behavior, or component functionality unless a concrete
release-integrity defect makes a narrowly bounded package-version correction
unavoidable.

## Initial Audit Result

The declaration-time read-only audit covered all 59 manifest entries and found:

- 58 intended id/version pairs absent from the public package feed, remote tags,
  and repository releases;
- one existing package, tag, and release:
  `FluxFlow.Mapping` / `1.0.3` / `mapping-v1.0.3`;
- the Mapping project remains a leaf package with no internal package
  dependency, and its current version intentionally remains `1.0.3`;
- the existing Mapping version is therefore a reused prerequisite, not a
  publication target; and
- publication must never invoke duplicate-skipping to make this distinction.

If subsequent package inspection proves that the current Mapping archive is not
compatible with the already published `1.0.3`, stop. Advance Mapping and every
dependent exact-version package deliberately, update changelogs and docs, and
repeat the affected package proof. Do not overwrite or silently reuse
incompatible bytes.

## Non-Negotiable Principles

1. Keep the implementation KISS, SRP, and explicit. Use small PowerShell
   helpers and existing repository conventions; add no dependency, reflection,
   hidden discovery framework, generic release engine, or service locator.
2. `eng/packages.json` remains the maintained package inventory. Project files
   remain the package-version and explicit internal-dependency source of truth.
3. Treat package publication as immutable. An unexpected existing id/version
   is a hard failure.
4. Do not use `--skip-duplicate` in the publication path.
5. Do not create or expose a repository release before the package has been
   published and verified from the public feed.
6. Preserve the existing release workflow's full build, solution tests, two
   real-provider suites, archive inspection, consumer smoke, release-note
   extraction, artifact upload, publication, indexing wait, and public-feed
   consumer verification.
7. Never log a package-feed secret, connection string, generated provider
   credential, or full secret value.
8. Keep ordinary CI server-free. External provider validation stays confined
   to the explicit release workflow and release-candidate proof.
9. Do not force-push, rewrite a tag, move a published tag, overwrite release
   assets for another commit, delete a public package, or use an administrative
   merge bypass.
10. Use normal branches, commits, pull requests, checks, and merges for code and
    evidence changes.
11. Update the goal, operator documentation, documentation site content where
    relevant, `memory/00-index.md`, `memory/01-current-state.md`, and a new
    numbered memory record.
12. Clean every temporary worktree, local package source, archive, cache, test
    server, and diagnostic file owned by this goal.

## Intended Publication Waves

The initial dependency calculation treats the already published `mapping`
alias as available and derives five deterministic waves from package-project
references. Exact package ids and versions must be resolved again from the
merged publication commit immediately before tagging.

### Reused prerequisite

- `mapping` (`FluxFlow.Mapping` 1.0.3)

### Wave 1

- `nodes`
- `resilience`

### Wave 2

- `components-assertions`
- `components-filesystem`
- `components-http`
- `components-mapping`
- `components-metrics`
- `components-observability`
- `components-payloads`
- `components-projections`
- `components-routing`
- `components-serialization`
- `components-sessions`
- `components-sources`
- `components-state`
- `components-storage`
- `components-timers`
- `components-validation`
- `composition`
- `coordination`

### Wave 3

- `components-designer`
- `components-expectations`
- `components-mqtt`
- `components-requestreply`
- `components-resilience`
- `components-storage-filesystem`
- `components-storage-sqlfile`
- `engine`
- `fluent`

### Wave 4

- `components-assertions-composition`
- `components-expectations-composition`
- `components-filesystem-composition`
- `components-http-aspnetcore`
- `components-http-composition`
- `components-mapping-composition`
- `components-metrics-composition`
- `components-mqtt-composition`
- `components-mqtt-mqttnet`
- `components-mqtt-pulsemqtt`
- `components-observability-composition`
- `components-payloads-composition`
- `components-projections-composition`
- `components-resilience-composition`
- `components-routing-composition`
- `components-serialization-composition`
- `components-sessions-composition`
- `components-sources-composition`
- `components-state-composition`
- `components-storage-composition`
- `components-timers-composition`
- `components-validation-composition`
- `engine-durable-input`
- `engine-durable-output`
- `fluent-hosting`

### Wave 5

- `engine-durable-input-sqlfile`
- `engine-durable-input-tsql`
- `engine-durable-output-sqlfile`
- `engine-durable-output-tsql`

The checked-in planning helper must reproduce these waves deterministically,
reject cycles and unknown already-available aliases, and list every new target
exactly once. Actual packed dependency metadata must be inspected during the
complete rehearsal. Any disagreement stops publication.

## Phase 1: Release-Safety Tooling

Add only the smallest cohesive helpers required by the concrete release risk:

1. A package availability helper that:
   - resolves a package through the existing manifest and project version;
   - resolves the configured package feed's V3 flat-container endpoint;
   - distinguishes exact-version `Missing` and `Present` states;
   - can require either state;
   - treats an invalid service index, missing flat-container resource,
     unexpected HTTP response, timeout, or protocol failure as an error rather
     than as `Missing`; and
   - prints stable machine-readable evidence without credentials.
2. A release-plan helper that:
   - reads the manifest and explicit `ProjectReference` relationships;
   - recognizes already-available manifest aliases explicitly;
   - ignores non-package project references only when they truly have no
     package identity;
   - rejects missing manifest projects, unknown aliases, duplicate assignment,
     and dependency cycles;
   - emits deterministic waves and an optional JSON representation; and
   - does not query the network or mutate Git.
3. Release-workflow ordering that is exactly:
   - resolve;
   - restore, build, and solution test;
   - real durable-input and durable-output validation;
   - pack, inspect, local consumer smoke, notes, and artifact upload;
   - require the public package version to be missing;
   - publish without duplicate skipping;
   - wait for public indexing and pass the public-feed consumer check; and
   - only then create or update the repository release.

Do not invent a second package manifest or duplicate package versions in a
release-train file. The goal records the accepted train; scripts calculate from
the existing authoritative inputs.

## Phase 2: Focused Tests And Documentation

Add focused Release-governance coverage for:

- availability `Missing`, `Present`, mismatch, malformed service index, and
  unreachable/failing endpoint behavior using deterministic test-owned input;
- real-repository five-wave planning with Mapping already available;
- plan rejection for an unknown available alias, missing project, and a
  synthetic dependency cycle;
- exact-once assignment of all intended package aliases;
- workflow ordering from availability through publication, feed verification,
  and release creation; and
- absence of `--skip-duplicate` from the release workflow.

Document:

- how to inspect the complete package inventory;
- how to run the availability audit;
- how to generate dependency waves;
- why manifest order is inventory order rather than guaranteed publication
  order;
- how to stop and resume after a failure without retagging or overwriting a
  version;
- how an operator handles the narrow case where package publication succeeded
  but indexing or repository-release creation failed; and
- the rule that a package is externally complete only after public-feed
  verification and repository-release creation both succeed.

## Phase 3: Pre-Merge Verification

Before committing release-safety changes:

1. Run the narrow Release test project.
2. Run package availability against deterministic test endpoints and the real
   public feed for all 59 intended versions.
3. Require exactly one accepted existing version (`mapping` 1.0.3) and exactly
   58 missing publication targets.
4. Require the planning helper to emit the accepted five waves with all 58
   targets exactly once.
5. Run Release governance.
6. Run a serialized warning-free Release build.
7. Run the complete Release solution tests.
8. Run formatting and direct/transitive vulnerability gates.
9. Repeat the complete 59-package local rehearsal from a new isolated source:
   preflight, prepare-only tag resolution, package/symbol creation, archive
   inspection, isolated-cache consumer loading, and local-feed verification.
10. Inspect packed internal dependencies and prove they are satisfied by the
    accepted waves plus the existing Mapping prerequisite.
11. Remove temporary resources and require a clean candidate status except for
    the intended tracked changes.

Any failure is a stop condition. Fix only its root cause, rerun the narrow
affected gate, then repeat the decisive governing gate.

## Phase 4: Normal Review And Merge

1. Inspect the complete diff and scan new names and user-facing text for neutral
   naming.
2. Commit only goal-owned files on `work/release-train-safety` with a neutral
   subject.
3. Push the branch and open a ready pull request against `main` with the scope,
   release risk, and verification evidence.
4. Require successful remote CI on the exact head.
5. Review all changed release logic and tests; resolve every actionable finding.
6. Merge normally using the repository's established merge-commit strategy.
7. Fast-forward local `main` and require it to equal `origin/main`.

No new package tag may be created before this merge completes.

## Phase 5: Publication Execution

At the exact clean merged commit:

1. Fetch remote tags and rerun the 59-entry package/tag/release/feed audit.
2. Require Mapping 1.0.3 to be the only existing intended version and require
   all 58 publication targets to remain absent.
3. Regenerate the five dependency waves and compare them with this goal.
4. For each wave:
   - resolve every alias and version from the exact commit;
   - run package preflight and prepare-only tag resolution;
   - create and push each annotated package tag through the existing helper;
   - never create the next dependent wave until every workflow in the current
     wave succeeds;
   - require each workflow to run the complete build/test/provider/package
     boundary;
   - require the exact public package version to index and load; and
   - require the repository release and its package/symbol assets to target the
     exact tagged commit.
5. Stop the train on the first failure. Do not push dependent-wave tags.
6. For an independent failure inside a wave, preserve successful immutable
   releases, diagnose the failed alias, and resume only that alias after the
   cause is fixed and the exact public state is re-audited.
7. Never reuse a version for changed bytes. If a package version was published
   successfully, any correction uses a new semantic version and advances only
   required dependents.

## Recovery Rules

- Tag creation fails before push: correct the local cause; no remote mutation
  exists.
- Tag push succeeds but workflow fails before package publication: retain the
  immutable tag, correct release tooling through a normal commit only if the
  package source is unchanged, and rerun the exact tagged workflow where
  supported. Never move the tag.
- Package publication succeeds but indexing times out: do not republish. Verify
  the exact version directly, then rerun or complete only the verification and
  repository-release portion using artifacts from the same workflow run.
- Package publication and feed verification succeed but repository-release
  creation fails: do not republish. Create/update only the matching repository
  release from the same workflow artifacts and exact tag.
- An unexpected version, tag, or release exists: stop and reconcile identity,
  target commit, assets, and feed state. Never classify it as success merely
  because a duplicate command returned zero.
- A package in a completed wave is immutable. Resume from the first incomplete
  alias/wave after re-auditing all prerequisites.

## Phase 6: Final Public Proof And Evidence

After all five waves complete:

1. Require all 59 manifest package versions to be available from the public
   feed: 58 newly published and Mapping 1.0.3 reused.
2. Require every new tag and repository release to exist and target the exact
   publication commit.
3. Require every repository release to contain one `.nupkg` and one `.snupkg`
   asset with the expected id/version names.
4. Run public-feed-only consumer verification for every manifest package using
   isolated caches and no local package source.
5. Run representative public-feed-only samples for Engine, Fluent Hosting,
   durable input with the SQL-file provider, and durable output with the
   SQL-file provider.
6. Record workflow run ids, tag targets, package counts, public-feed results,
   consumer results, failures/recoveries, and cleanup evidence in this goal and
   a new memory record.
7. Update documentation and memory indexes/current state.
8. Commit evidence on a neutral documentation branch, open a normal pull
   request, require CI, merge normally, and synchronize local `main`.

The evidence-only merge does not require republishing packages or moving tags.
Package tags must continue to point at the exact code-bearing publication
commit.

## Acceptance Criteria

The goal is complete only when all of the following are true:

- the release-safety implementation is small, explicit, dependency-free, and
  covered by focused tests;
- the workflow fails before publication when an intended version already
  exists;
- the workflow contains no duplicate-skipping publication option;
- public package publication precedes feed verification, and feed verification
  precedes repository-release creation;
- the planning helper emits five dependency-safe waves with Mapping reused;
- the local and remote code-review gates pass;
- release-safety changes merge normally before publication;
- all 58 new package versions publish from one exact clean merged commit;
- Mapping 1.0.3 is reused without mutation;
- all 59 intended versions restore and load from the public feed through
  isolated package caches;
- all 58 new tags and releases target the exact publication commit and contain
  the expected package and symbol assets;
- no dependent wave begins before its prerequisites complete;
- no credential, provider resource, temporary worktree, package source, cache,
  or diagnostic artifact leaks;
- goal, docs, memory index, current state, and a new numbered memory record
  contain exact final evidence; and
- local `main` is clean, synchronized with `origin/main`, and the active goal is
  marked complete.

## Completion Evidence

### Pre-merge candidate proof

Observed on `work/release-train-safety` before commit and remote review:

- the public-feed audit covered all 59 manifest entries and required the exact
  accepted state: 58 missing publication targets and one present reused
  prerequisite (`mapping` / `FluxFlow.Mapping` 1.0.3);
- the release planner produced 5 waves, assigned all 58 new targets exactly
  once, and treated Mapping as already available;
- package preflight passed 59/59 entries;
- prepare-only tag resolution passed 59/59 entries without creating a tag;
- Release governance passed 141/141 tests with zero warnings;
- formatting verification passed with no change;
- restore covered 134 projects with zero warnings or errors;
- the serialized Release build covered 134 projects with zero warnings or
  errors;
- the complete Release solution passed 2,509/2,509 tests across 66 projects
  with zero warnings;
- the direct/transitive package vulnerability audit reported no vulnerable
  package for any solution project;
- the isolated all-package rehearsal passed 59/59 package dry runs and produced
  exactly 59 package archives plus 59 symbol archives;
- archive inspection found 119 distinct internal dependency edges, and every
  edge used the exact manifest project version and pointed to an earlier
  publication wave; and
- the rehearsal-owned temporary package source and consumer caches were
  removed successfully.

The following sections record publication, workflow ids, release assets,
public-feed consumers, final documentation, memory, and cleanup evidence. The
evidence-only review and merge remain before this goal can be marked complete.

### Release-safety merge

- Pull request 69 merged normally before any new tag was created.
- The release-safety implementation commit was
  `71a047047b17cc7b1128b5b6a96a9a55ac5a8fd4`.
- The exact code-bearing publication merge commit was
  `d54f1f4ad91cfe408bad8d4bb74f6194323db2fd`.
- Post-merge audit required Mapping 1.0.3 to be the only available intended
  version and all 58 new targets to remain absent before tagging.
- The checked-in planner reproduced all five accepted waves with Mapping
  explicitly reused.

### Publication execution

- Wave 1 published and independently verified 2/2 packages.
- Wave 2 published and independently verified 18/18 packages.
- Wave 3 published and independently verified 9/9 packages.
- Wave 4 published and independently verified 25/25 packages. It ran as
  dependency-independent sub-batches of 8, 8, and 9 after earlier provider-test
  load sensitivity was observed.
- Wave 5 published and independently verified 4/4 provider packages.
- No dependent wave began before every prerequisite package was public,
  restorable, and represented by its exact release and assets.

Four workflows failed before publication. Their publish, feed-verification, and
release-creation steps were skipped. Exact package and release absence was
proved before the same immutable tagged workflow was rerun:

- `components-validation` / `30786674537`: component-event timing test timeout;
  the isolated retry succeeded.
- `components-timers` / `30786663467`: request/reply timing test timeout; the
  isolated retry succeeded.
- `components-http` / `30786539863`: durable-input provider concurrency result
  88/89 with one owner receiving 0 rows instead of 5; the isolated retry passed
  all 89 provider cases.
- `components-resilience-composition` / `30794438388`: the same load-sensitive
  provider concurrency result 88/89; exact public/release absence was confirmed
  and the isolated retry passed both provider suites and completed in 21m40s.

No successful immutable package was rerun, replaced, or republished.

### Successful workflow run ids

Reruns retain the same run id. These ids represent the final successful attempt
for every new package:

```text
Wave 1
nodes=30785531764
resilience=30785543525

Wave 2
components-assertions=30786519533
components-filesystem=30786529442
components-http=30786539863
components-mapping=30786549342
components-metrics=30786559309
components-observability=30786568716
components-payloads=30786578386
components-projections=30786588571
components-routing=30786598879
components-serialization=30786608642
components-sessions=30786620187
components-sources=30786629790
components-state=30786641449
components-storage=30786652427
components-timers=30786663467
components-validation=30786674537
composition=30786684739
coordination=30786694285

Wave 3
components-designer=30791263803
components-expectations=30791276653
components-mqtt=30791288462
components-requestreply=30791298779
components-resilience=30791310744
components-storage-filesystem=30791322363
components-storage-sqlfile=30791339010
engine=30791353574
fluent=30791365780

Wave 4 batch 1
components-assertions-composition=30792743037
components-expectations-composition=30792760761
components-filesystem-composition=30792783492
components-http-aspnetcore=30792805375
components-http-composition=30792824552
components-mapping-composition=30792850043
components-metrics-composition=30792863326
components-mqtt-composition=30792877850

Wave 4 batch 2
components-mqtt-mqttnet=30794358027
components-mqtt-pulsemqtt=30794372424
components-observability-composition=30794386909
components-payloads-composition=30794400014
components-projections-composition=30794424918
components-resilience-composition=30794438388
components-routing-composition=30794451343
components-serialization-composition=30794479647

Wave 4 batch 3
components-sessions-composition=30797443849
components-sources-composition=30797468023
components-state-composition=30797493295
components-storage-composition=30797511263
components-timers-composition=30797551466
components-validation-composition=30797588532
engine-durable-input=30797605852
engine-durable-output=30797638797
fluent-hosting=30797656101

Wave 5
engine-durable-input-sqlfile=30799299931
engine-durable-input-tsql=30799345173
engine-durable-output-sqlfile=30799400344
engine-durable-output-tsql=30799444345
```

### Final public proof

- 58/58 new workflow runs completed successfully on publication commit
  `d54f1f4ad91cfe408bad8d4bb74f6194323db2fd`.
- 58/58 new tags and repository releases target that commit.
- 58/58 new releases contain exactly the expected package and symbol assets.
- Mapping 1.0.3 remained unchanged and was reused without publication.
- 59/59 project-declared manifest versions are present on the public feed.
- 59/59 isolated public-feed-only consumers restored and loaded successfully.
- A separate public-feed-only executable resolved Engine, ran a hosted Fluent
  graph from `public-feed` to `PUBLIC-FEED`, and performed real SQL-file
  durable-input and durable-output enqueues.
- The executable proof project, isolated package cache, binaries, SQL-file
  databases, and temporary directories were removed. No proof-owned temporary
  resource remained.

The evidence-only documentation branch and its normal review/merge remain the
only incomplete operational step. Package tags stay fixed to the code-bearing
publication commit and require no republishing.
