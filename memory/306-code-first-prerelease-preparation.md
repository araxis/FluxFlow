# Code-First Prerelease Preparation

Date: 2026-08-08

## Decision

Release the completed code-first simplification as a coordinated prerelease
before stable promotion. The external pilot proved locally packed candidate
bytes, but the final acceptance boundary is a separate application restoring
the exact prerelease from the public package feed.

## Package closure

Repository diff and package-project reference analysis found 27 directly
changed package projects. Reverse dependency closure adds the SQL-file and
networked relational durable input/output providers. The final release target
is exactly 31 packages; the remaining 29 manifest packages are reused at their
published versions.

All affected packages after 1.0 use a next-major `rc.1` version. The new
`FluxFlow.Engine.HealthChecks` package uses `1.0.0-rc.1` with an explicit null
binary-compatibility baseline because it has no published predecessor.

The planner produces four waves:

1. Composition.
2. Designer and Engine.
3. Nineteen component-composition packages, durable input/output cores,
   health checks, and Fluent.
4. Four durability providers and Fluent Hosting.

## Migration boundary

The release guide makes the intended breaks explicit:

- complete `ComponentContract` values replace split authoring/runtime
  declarations;
- port mappings use `HasInput`, `HasSignalInput`, `HasOutput`, and `HasEvents`;
- Events are explicit named outputs;
- typed C# application builders capture handles and connections;
- code-first definitions execute with one `AddFluxFlow(definition)` call;
- JSON applications retain explicit package registration and data-only hot
  reload;
- raw dynamic registration lives under `Advanced.AddDynamicComponent`;
- resources are definition-owned through `ApplicationResourceContract`;
- typed runtime/durability operations reuse canonical addresses; and
- Fluent is a facade over the canonical Engine lifecycle.

## Publication security

The existing release workflow used a long-lived repository API-key secret.
Preparation migrates it to the `release` environment, `id-token: write`, a
trusted package-feed login action, `secrets.NUGET_USER`, and the generated
short-lived token.
The existing exact-absence, no-duplicate-skip, public verification, and release
ordering remain unchanged.

The feed-side trusted-publishing policy must match owner `araxis`, repository
`FluxFlow`, workflow file `publish-nuget.yml`, and environment `release` before
publication. The previous repository secret is retained until trusted
publication succeeds; deleting it is outside this goal.

## Promotion rule

Prerelease tags and package versions are immutable. Stable promotion creates new
stable versions from a separately reviewed commit after all public packages are
indexed and the external package-only pilot passes code-first, readiness, JSON
rollback, retained-route, and two-process SQL-file durability scenarios using
the public feed only.

## Current status

Versions, changelog entries, migration documentation, release plan, and trusted
workflow migration are prepared locally. Exact local evidence is:

- 31/31 metadata and changelog preflights;
- 31/31 public-absence checks;
- 193/193 Release-governance tests;
- 137-project warning-free restore and CI-style Release build;
- 2,677/2,677 solution tests across 67 projects;
- 31/31 compatibility-aware package operations using isolated public
  baselines, with narrow reviewed suppressions only in Composition, Designer,
  and Fluent;
- 31/31 archive inspections and 31/31 isolated package smoke consumers;
- one warning-free package-only behavioral build and complete Engine,
  code-first, resource, readiness, Fluent, durability, JSON rollback, and
  restart-recovery marker set;
- clean solution formatting and whitespace; and
- no known vulnerable direct or transitive dependency.

Publication remains blocked until the feed-side trusted policy and
`NUGET_USER` environment secret are confirmed, followed by remote review/merge
of the exact release commit.
