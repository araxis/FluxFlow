# Release-Candidate Consolidation

Date: 2026-08-08

The accumulated typed authoring, portable JSON, resources, durability, health,
performance, package-acceptance, sample, and documentation work was
consolidated on `work/release-candidate-consolidation`.

The exact implementation candidate is
`4bf69015b9d3eaa95a45630c91d378c45c5a2aaa`. It was validated from a detached
clean worktree, not from the primary dirty tree.

## Final supported model

- Typed C# code-first definitions retain complete executable component and
  resource contracts. Normal hosting registers the definition once with
  `AddFluxFlow(definition)` and does not repeat ordinary family registration.
- Portable JSON remains an independent data-only definition path. Its runtime
  component/resource catalog is explicit, keeping configuration portable and
  avoiding reflection, discovery magic, or hidden activation.
- C# authoring may use executable C# predicates and is not constrained by JSON
  serialization. JSON remains the format for configuration, persistence,
  designers, and hot reload.
- Both paths execute through the canonical Engine lifecycle and revision model.

## Consolidation changes

- The package-only JSON scenario now proves same-definition `Unchanged`,
  independent invalid-candidate `Rejected`, exact active-revision retention,
  and a fresh successful route after rejection.
- All 19 component-family package READMEs explain contract-owned executable
  descriptors, one-line normal code-first registration, and the explicit
  portable/dynamic registration boundary.
- Release-governance tests enforce those package documentation and package-only
  behavior boundaries.

## Exact evidence

- Restore: 137 projects, successful.
- CI-style Release build: 137 projects, 0 warnings, 0 errors.
- Solution: 2,675 passed across 67 test projects, 0 failures, 0 warnings.
- Release tests: 191 passed, 0 failures, 0 warnings.
- Public API baseline: 2 passed, 0 failures, 0 warnings.
- Formatting and whitespace: clean.
- Direct/transitive vulnerability audit: no vulnerable packages.
- Package-only consumer: ten exact candidate packages, 15 process invocations,
  all required markers exactly once, and owned source/work directories removed.
- Real T-SQL durable input: 90 passed, 0 failed, 0 skipped.
- Real T-SQL durable output: 117 passed, 0 failed, 0 skipped.
- Both provider gates used the pinned provider-image digest
  `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`.

No push, pull request, tag, release, package publication, or external product
mutation occurred. Publication remains a separately authorized step.
