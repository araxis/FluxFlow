# Goal: Publish and externally verify the code-first simplification prerelease

- Date: 2026-08-08
- State: in progress
- Scope: release preparation, package version freeze, migration guidance,
  trusted publication, public-feed verification, and external pilot validation
- Candidate branch: `work/release-candidate-consolidation`
- Candidate implementation commit: `4bf69015b9d3eaa95a45630c91d378c45c5a2aaa`
- Compatibility: intentional breaking release for the affected package closure
- Publication: prerelease only until the public-feed pilot passes

## Objective

Publish the completed FluxFlow simplification as one dependency-safe release
candidate and prove it from a separate package-only application using the public
package feed.

The outcome must preserve the framework's two first-class authoring paths:

1. typed C# code-first applications use complete component and resource
   contracts, typed handles, C# predicates, and one `AddFluxFlow(definition)`
   registration; and
2. portable JSON applications use explicit package registration, remain
   serializable, and retain validation, persistence, and hot-reload behavior.

The release must not reintroduce duplicate registration, reflection-driven
discovery, a parallel Fluent runtime, hidden workers, or backend configuration
inside application options.

## Principles

- Keep the normal API flat, explicit, typed, and definition-first.
- Treat breaking public APIs and breaking package dependency floors as major
  releases after 1.0.
- Publish one immutable version per package and never reuse or move a successful
  package tag.
- Calculate release waves from actual package project references.
- Require exact public absence before publication and exact public presence,
  restore, and execution afterward.
- Keep publication fail-closed. Authentication, indexing, package identity,
  hash, release-record, or pilot ambiguity is a stop condition.
- Use short-lived trusted publishing credentials. Do not publish with a
  long-lived package-feed API key.
- Keep the old repository secret until trusted publishing has succeeded; secret
  deletion is outside this goal.
- Promote the same reviewed source and behavior to stable versions only after
  prerelease acceptance. Stable promotion is a separate versioned release.

## Frozen package closure and versions

The direct source changes affect 27 package projects. Four dependent packages
must also move so their package dependency floors reference the new major line.
The resulting closure is exactly 31 packages.

| Package | Previous | Prerelease |
| --- | ---: | ---: |
| FluxFlow.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Designer | 5.0.0 | 6.0.0-rc.1 |
| FluxFlow.Components.Assertions.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Expectations.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.FileSystem.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Http.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Mapping.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Metrics.Composition | 5.0.0 | 6.0.0-rc.1 |
| FluxFlow.Components.Mqtt.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Observability.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Payloads.Composition | 5.0.0 | 6.0.0-rc.1 |
| FluxFlow.Components.Projections.Composition | 5.0.0 | 6.0.0-rc.1 |
| FluxFlow.Components.Resilience.Composition | 4.0.0 | 5.0.0-rc.1 |
| FluxFlow.Components.Routing.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Serialization.Composition | 5.0.0 | 6.0.0-rc.1 |
| FluxFlow.Components.Sessions.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Sources.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.State.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Storage.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Timers.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Components.Validation.Composition | 6.0.0 | 7.0.0-rc.1 |
| FluxFlow.Engine | 7.0.0 | 8.0.0-rc.1 |
| FluxFlow.Fluent | 4.0.0 | 5.0.0-rc.1 |
| FluxFlow.Fluent.Hosting | 4.0.0 | 5.0.0-rc.1 |
| FluxFlow.Engine.DurableInput | 1.3.0 | 2.0.0-rc.1 |
| FluxFlow.Engine.DurableInput.SqlFile | 1.3.0 | 2.0.0-rc.1 |
| FluxFlow.Engine.DurableInput.TSql | 1.2.0 | 2.0.0-rc.1 |
| FluxFlow.Engine.DurableOutput | 3.0.0 | 4.0.0-rc.1 |
| FluxFlow.Engine.DurableOutput.SqlFile | 3.0.0 | 4.0.0-rc.1 |
| FluxFlow.Engine.DurableOutput.TSql | 2.0.0 | 3.0.0-rc.1 |
| FluxFlow.Engine.HealthChecks | unpublished | 1.0.0-rc.1 |

The other 29 manifest packages remain at their published versions. They are
not rebuilt under new identities merely to create a uniform train.

## Dependency waves

Generate the plan from `eng/package-release-plan.ps1`, marking the 29 unchanged
aliases already available. The required result is:

1. Wave 1: `composition`.
2. Wave 2: `components-designer`, `engine`.
3. Wave 3: the 19 component-composition packages plus
   `engine-durable-input`, `engine-durable-output`, `engine-healthchecks`, and
   `fluent`.
4. Wave 4: both durable-input providers, both durable-output providers, and
   `fluent-hosting`.

Do not begin a dependent wave until every package in the preceding wave is
indexed, independently restorable, and represented by an exact repository
release and assets.

## Phase 1: Release metadata and migration guide

1. Update each affected project-owned `<Version>` to the frozen prerelease.
2. Keep every `binaryCompatibilityBaseline` at the latest published stable
   contract; keep the new health package baseline explicitly `null`.
3. Add a non-empty package-specific changelog section for every prerelease.
4. Add a migration guide that maps retired or discouraged surfaces to the
   supported code-first, JSON, and advanced paths.
5. Document that C# definitions are executable in-memory blueprints and are not
   required to serialize to JSON.
6. Document the 31-package closure, four dependency waves, public-feed pilot,
   stable-promotion boundary, and failure recovery rules.
7. Update the docs index, memory index, and current-state record.

## Phase 2: Trusted publication

Migrate `.github/workflows/publish-nuget.yml` from the long-lived API-key secret
to trusted publishing:

- use the exact existing workflow filename `publish-nuget.yml`;
- use the repository `araxis/FluxFlow`;
- use the `release` environment;
- grant `id-token: write` and the existing required repository-content access;
- call the trusted package-feed login action immediately before the publish
  step;
- pass `secrets.NUGET_USER` as the package-feed profile name;
- use the short-lived token output for `dotnet nuget push`;
- preserve the repository's exact-absence check and prohibition on duplicate
  skipping;
- verify the public feed before creating the repository release.

The feed-side trusted-publishing policy and `NUGET_USER` environment secret must
exist before the first publication run. Do not delete the existing API-key
secret during this goal.

## Phase 3: Local validation

Before pushing:

1. verify manifest/project/changelog consistency;
2. run the focused release workflow and package policy tests;
3. restore and build the full solution in Release with CI build settings;
4. run the complete solution and Release-governance tests;
5. run the package-only candidate acceptance gate;
6. run package archive inspection and package smoke checks for the affected
   closure;
7. run binary compatibility preflight using each frozen published baseline;
8. run formatting, whitespace, and dependency-vulnerability checks;
9. keep all validation outputs outside source or in ignored artifact paths; and
10. record exact counts, warnings, failures, package identities, and cleanup.

Intentional major-version breaks must be reviewed, not hidden. If the package
compatibility tool rejects a deliberate removal, use a narrow reviewed
suppression owned by the affected project only; never disable package
validation globally.

## Phase 4: Review and merge

1. Commit only the release preparation on the existing neutral branch.
2. Push the branch with upstream tracking.
3. Open one pull request describing the authoring simplification, migration,
   package impact, trusted publishing change, and validation evidence.
4. Require all remote checks to pass on the exact head.
5. Merge through the repository's normal protected workflow.
6. Record the immutable merged publication commit. All prerelease tags must
   target that exact commit.

## Phase 5: Prerelease publication

For each dependency wave:

1. prove every target version is absent from the public feed;
2. create and push each guarded package tag separately so events are not lost;
3. wait for the exact release workflow to finish;
4. verify package and symbol assets, public indexing, isolated restore/load, and
   repository release target;
5. record the workflow run id and result; and
6. stop on any ambiguous publication state.

If a run fails before publication, prove both the exact package version and
repository release are absent before rerunning the unchanged tag. If upload
succeeds but indexing or release creation fails, never republish or move the
tag; resume only the incomplete post-publication operation from retained
artifacts.

## Phase 6: External public-feed pilot

Update the separate `C:\Projects\FluxFlow.Pilot` repository to reference the
exact prerelease versions and use the public feed only. It must have no source
repository project references and no candidate package directory.

The pilot must prove:

- typed code-first application construction with complete contracts;
- one `AddFluxFlow(definition)` registration and no duplicate ordinary
  component/resource registration;
- typed send/receive and clean lifecycle;
- optional readiness behavior;
- portable JSON startup, unchanged reapply, rejected invalid candidate,
  retained active revision, and successful post-rejection routing;
- SQL-file durable input/output state across separate seed and recovery
  processes; and
- cleanup of pilot-owned package caches, databases, binaries, and temporary
  directories.

## Stable promotion boundary

Do not overwrite or relabel prerelease package versions. When the public-feed
pilot and an agreed observation period are complete, create stable project
versions without the `-rc.1` suffix, add stable changelog sections, rerun the
same dependency-wave checks, and publish new immutable stable package versions.

## Non-goals

- No new workflow semantics, components, providers, persistence abstractions,
  ORMs, background workers, reflection, scanning, or runtime dependencies.
- No attempt to serialize executable C# delegates or resource factories into
  portable JSON.
- No republishing of the 29 unaffected stable packages.
- No automatic deletion of the previous package-feed credential.
- No stable release before public prerelease evidence is complete.

## Acceptance criteria

- Exactly 31 affected project versions and changelog sections match the frozen
  matrix.
- The dependency planner returns exactly the four frozen waves.
- Release workflow governance proves trusted short-lived publication and the
  existing fail-closed ordering.
- Local build, tests, release governance, package rehearsal, format, whitespace,
  vulnerability, archive, and compatibility gates pass without warnings or
  unexplained suppressions.
- One reviewed immutable commit is used for every prerelease tag.
- Every prerelease package is public, independently restorable, hash-identifiable,
  and represented by the matching repository release and assets.
- The separate public-feed-only pilot passes all five acceptance facts and
  removes its owned temporary state.
- Documentation and memory identify the exact publication commit, pull request,
  workflow runs, package versions, pilot commit, evidence, failures, recoveries,
  and stable-promotion decision.

## Local preparation evidence

The prepared working tree has completed the local pre-publication boundary:

- all 31 release metadata/changelog preflights passed;
- all 31 exact prerelease versions were confirmed absent from the public feed;
- Release governance passed 193/193 with zero warnings;
- solution restore completed for 137 projects with zero warnings;
- the CI-style Release build completed for 137 projects with zero warnings and
  zero errors;
- solution tests passed 2,677/2,677 across 67 projects with zero warnings;
- the pack-mode behavioral acceptance fact passed;
- compatibility-aware package creation passed for all 31 targets against their
  exact isolated public baselines;
- only Composition, Designer, and Fluent required reviewed project-owned
  compatibility suppressions for intentional major-version breaks;
- the combined source contains 31 package archives and 31 symbol archives;
- archive inspection and isolated package smoke passed 31/31;
- the representative package-only application restored exact candidate bytes,
  built without warnings, and passed Engine, code-first, resource, health,
  Fluent, SQL-file durability, JSON rollback, and restart recovery markers;
- full-solution formatting and whitespace verification passed; and
- the complete direct/transitive dependency scan reported no known vulnerable
  packages.

Remote review, trusted feed-policy confirmation, publication, and public-feed
pilot execution remain pending.
