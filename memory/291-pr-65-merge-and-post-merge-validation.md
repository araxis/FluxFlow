# Pull Request 65 Merge And Post-Merge Validation

Date: 2026-08-02

## Outcome

Pull request 65 was merged normally at the exact reviewed head, then validated
as a release candidate without creating a tag, release, or package
publication. The process found two validation defects, corrected each through a
small normal pull request, and repeated the affected gates. The final merged
state passed the complete solution, release governance, formatting,
vulnerability, real-provider, and 59-package rehearsal boundaries.

## Merge Integrity

- Reviewed head: `650dc1b916f3fee3d2f03d2a043be0312b45a7b8`.
- Captured base: `6ffc668b8054848f6c8e637005b10bcb4ee96689`.
- Exact-head CI: run `30753529432`, successful.
- Approval attempt: rejected because the authenticated author cannot approve
  their own pull request; no bypass or alternate identity was used.
- Normal merge: `7e64962256dbef873120e3dd34635dc13c24b4fb`.
- Merge parents match the captured base and reviewed head.
- Merge tree and reviewed-head tree both equal
  `163cb252abe4eae66a7338d3e6370b26a83032e5`.

## Causal Test Stabilization

The first clean full run exposed a load-sensitive source-completion test. The
test requested completion and immediately released target capacity without
awaiting the blocked third emission's cancellation exit. One of 12 concurrent
focused processes reproduced the timeout; the production cancellation
primitive already had direct behavioral coverage.

Pull request 66 added one explicit test-owned exit signal. It retained exact
accepted output `[1, 2]` and added no production change, sleep, retry, or timeout
increase. Head `0240d84f07548eaf3d223da4add586501adc4ed6` passed CI run
`30755487188` and merged normally as
`f3907fc0478957c31be8baa5662f85c360ea4580`.

## Runtime And Real-Provider Proof

The resulting runtime-bearing commit passed in a clean detached worktree:

- 134-project restore and serialized Release CI build, zero warnings/errors;
- 2,495/2,495 Release tests across 66 projects, zero warnings;
- 127/127 Release-governance tests;
- solution formatting and vulnerability scan;
- durable input 89/89, zero skips;
- durable output 117/117, zero skips; and
- tested server digest
  `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`.

Both integration runners removed their owned containers.

## Package Cache Isolation Finding

The first all-package rehearsal passed 53 consumer runs and then failed while
loading `FluxFlow.Engine.DurableInput`. The candidate Engine archive correctly
declared `FluxFlow.Nodes` 4.0.0, but the consumer reused a same-version Engine
entry from the machine-wide package cache whose older dependency graph selected
Nodes 3.0.1. The archive was correct; the smoke environment was not isolated.

Pull request 67 gave every consumer-smoke work directory its own package cache,
disabled cache reuse during restore, reported the cache path, and asserted that
contract in release tests. This mirrors the existing feed verifier without a
new dependency or abstraction. Focused tests passed 2/2, the exact failing
consumer passed, governance passed 127/127, and CI run `30758310177` succeeded
on head `b8b26ae7edda7167601c6d660c76b7536edfeee1`. It merged normally as
`ceedc36f6be53c11c89ddc49fe8f8b8fd427d227`.

## Final Exact-Commit Proof

The final merge passed from a newly created clean detached worktree:

- restore: 134 projects, zero warnings/errors;
- serialized Release build: 134 projects, zero warnings/errors;
- decisive full suite: 2,495/2,495 across 66 projects, zero warnings;
- Release governance: 127/127;
- full formatting and direct/transitive vulnerability gates;
- 59/59 package preflights;
- 59/59 prepare-only tag resolutions;
- 59 package and 59 symbol archives in a fresh source; and
- 59/59 isolated-cache dry-runs, each covering archive inspection, symbol
  validation, package consumer restore/build/load, and local feed verification.

A first exact-commit full-suite attempt timed out once in an observation test
during local host contention. That test passed 20/20 consecutive focused runs,
remote CI was green on the exact commit, and the decisive next complete run
passed all 2,495 tests. No weakening change was made.

## Cleanup And Boundary

All detached worktrees, temporary package feeds, 118 archives, caches,
diagnostics, logs, and helper scripts were removed after their counts were
recorded. Git was clean, no goal-owned container remained, no tag points at the
final commit, and no tag, release, publication, public-feed mutation, or release
workflow dispatch occurred.

The next action is a separate explicit release/tag decision. This validation
does not authorize or imply publication.
