# Coordinated Release Train

Date: 2026-08-03

## Outcome

The canonical 59-package FluxFlow inventory is available from the public
package feed. Fifty-eight versions were newly published from one immutable code
commit, `d54f1f4ad91cfe408bad8d4bb74f6194323db2fd`. `FluxFlow.Mapping` 1.0.3 was
the one audited, already-available prerequisite and was reused without
republishing or moving its tag.

Every new tag and repository release targets the publication commit. Every
release contains exactly one package archive and one symbol archive named for
the expected package id/version. All 59 manifest packages restore and load from
isolated public-feed-only consumers.

## Release Safety Boundary

Pull request 69 merged the fail-closed release changes before any new tag was
created. The small release boundary now:

- resolves package identity from `eng/packages.json` plus the project version;
- distinguishes exact public `Missing` and `Present` states and treats network
  or protocol ambiguity as failure;
- derives deterministic dependency waves from explicit package-project
  references;
- requires public absence immediately before publication;
- publishes without duplicate skipping;
- verifies public indexing and an isolated public consumer before creating the
  repository release; and
- preserves the complete solution, both real-provider suites, archive,
  consumer, notes, and artifact checks.

The safety merge was `d54f1f4ad91cfe408bad8d4bb74f6194323db2fd` and contains
the code-bearing release state. Package tags remain fixed to that commit;
evidence-only documentation does not move or republish them.

## Dependency-Safe Execution

The planner reused `mapping` and emitted five waves containing all 58 new
targets exactly once:

- Wave 1: 2 packages.
- Wave 2: 18 packages.
- Wave 3: 9 packages.
- Wave 4: 25 packages, executed as bounded independent sub-batches of 8, 8,
  and 9 after load-sensitive provider behavior was observed.
- Wave 5: 4 provider packages.

No dependent wave began before its prerequisite packages were public,
restorable, and represented by exact repository releases and assets.

## Failures And Recovery

Four failures occurred before publication. In every case the publish, public
verification, and release steps were skipped; the exact package version was
confirmed absent and the repository release was confirmed missing before the
same immutable tagged workflow was rerun.

- `components-validation`, run `30786674537`: an unrelated component-event
  timing test timed out. Its isolated retry succeeded.
- `components-timers`, run `30786663467`: an unrelated request/reply timing
  test timed out. Its isolated retry succeeded.
- `components-http`, run `30786539863`: the real durable-input multi-owner
  concurrency check completed 88/89 cases, with one owner observing 0 rows
  instead of 5. Its isolated retry passed all 89 cases.
- `components-resilience-composition`, run `30794438388`: the same
  load-sensitive durable-input concurrency check completed 88/89 cases. The
  exact version and release were absent; a single isolated retry passed both
  provider suites and completed publication in 21m40s.

Successful immutable releases were never rerun or replaced. Wave 4's bounded
sub-batches kept the recovery boundary explicit and avoided reopening already
verified packages.

## Workflow Run Evidence

The final successful attempt for every new package is recorded below. A rerun
retains the same workflow run id.

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

Wave 4, batch 1
components-assertions-composition=30792743037
components-expectations-composition=30792760761
components-filesystem-composition=30792783492
components-http-aspnetcore=30792805375
components-http-composition=30792824552
components-mapping-composition=30792850043
components-metrics-composition=30792863326
components-mqtt-composition=30792877850

Wave 4, batch 2
components-mqtt-mqttnet=30794358027
components-mqtt-pulsemqtt=30794372424
components-observability-composition=30794386909
components-payloads-composition=30794400014
components-projections-composition=30794424918
components-resilience-composition=30794438388
components-routing-composition=30794451343
components-serialization-composition=30794479647

Wave 4, batch 3
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

## Final Public Proof

- 58/58 new workflow runs completed successfully on the publication commit.
- 58/58 new tags and releases target the publication commit.
- 58/58 releases contain the exact package and symbol assets.
- 59/59 project-declared manifest versions are present publicly.
- 59/59 isolated public-feed-only consumers restored and loaded.
- A separate public-only executable resolved Engine, ran a hosted Fluent graph
  from `public-feed` to `PUBLIC-FEED`, and performed real SQL-file durable-input
  and durable-output enqueues.
- The executable proof's project, package cache, binaries, SQL-file databases,
  and temporary directories were removed; no proof-owned temporary resource
  remained.

Pre-publication candidate proof also passed 141/141 Release-governance tests,
a warning-free 134-target serialized Release build, 2,509/2,509 solution tests
across 66 projects, formatting and vulnerability gates, and the complete
59-package rehearsal with exact dependency-wave inspection.

## Durable Decision

Keep publication immutable and dependency-aware. Reuse an existing package
only after an exact identity/compatibility audit. A pre-publication failure may
rerun its unchanged immutable tag only after feed and release absence are
proven. A successful publication is never rerun; incomplete indexing or release
record creation resumes only the incomplete post-publication operation.
