# Release Validation

FluxFlow keeps ordinary continuous integration server-free. The normal workflow
restores, builds, and tests `FluxFlow.sln`; local SQL-file providers need no
external infrastructure, and the two real networked-relational integration
projects intentionally remain outside the solution.

The release workflow adds both real-provider suites after the normal solution
tests and before package creation or publication. A provider failure therefore
stops the release before an artifact can be published.

## Local real-provider validation

Run the durable-input suite from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/FluxFlow.Engine.DurableInput.TSql.IntegrationTests/run-integration.ps1 -AcceptLicense
```

Run the durable-output suite separately:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/FluxFlow.Engine.DurableOutput.TSql.IntegrationTests/run-integration.ps1 -AcceptLicense
```

The explicit `-AcceptLicense` switch confirms acceptance of the container image
license. Each runner owns a uniquely named disposable server, an ephemeral
credential, a random loopback port, isolated test databases, a bounded readiness
window, a temporary result directory, and cleanup in `finally`. It requires at
least one executed test and rejects any skipped or failed test. Output includes
the tested image tag and digest but excludes credentials and full connection
strings.

For a CI-managed server, set
`FLUXFLOW_TSQL_INTEGRATION_CONNECTION_STRING` in the process environment and add
`-UseExternalConnectionString`. Do not put a real connection string in a command,
repository file, log, or workflow definition. The configured identity must own
the create/alter/drop lifecycle of the isolated test databases.

`-KeepContainer` is for deliberate local diagnostics only. When supplied, the
runner prints the retained name and the caller becomes responsible for removing
that container. It must not be used in release validation.

Provider-specific setup and behavior remain documented in
[T-SQL durable inputs](34-tsql-durable-inputs.md),
[T-SQL durable outputs](32-tsql-durable-outputs.md), and the integration-project
READMEs.

## Clean final proof

For a large accumulated change, validate the committed `HEAD` from a detached
temporary worktree. Restore, build with `ContinuousIntegrationBuild=true`, run
the complete solution tests and Release governance, verify formatting and
dependencies, then run both real-provider suites sequentially. The detached
worktree proves that committed files are sufficient and that the long-lived
workspace is not supplying hidden untracked inputs.

Remove the detached worktree only through the repository worktree command after
verification. Confirm both integration runners removed their owned containers
and that the detached worktree has no repository changes.

## Complete package rehearsal

For a manifest-wide release rehearsal, use a new temporary package source and
process `eng/packages.json` in order. For each alias, run release preflight,
resolve the tag with `package-release-tag.ps1 -PrepareOnly`, and pack the
already built project. Require one package archive and one symbol archive for
every manifest entry before consumer verification begins.

Then run `package-release-dry-run.ps1` for every alias with
`-SkipSolutionBuild` and the temporary directory as `-PackageSource`. The dry
run inspects package and symbol contents, restores and loads the candidate from
a package-only consumer, and verifies the local feed. The consumer and feed
helpers use work-directory-local package caches; this prevents an installed
same-id/same-version package from substituting stale dependency metadata for
the candidate archive.

Preparation and dry-run commands do not create a tag, release, or publication.
Remove the temporary source and consumer caches only after recording the exact
preflight, prepare, archive, symbol, and `DRY_RUN_OK` counts.

## Publication integrity

The release workflow requires the resolved id/version to be absent from the
public package feed immediately before publication. It publishes without a
duplicate-skipping option, waits for public indexing, and runs the isolated
public-feed consumer check before creating or updating the repository release.
This ordering prevents an unavailable package from being represented by a
successful public repository release.

For a coordinated train, generate dependency waves explicitly:

```powershell
./eng/package-release-plan.ps1 -AlreadyAvailable mapping
```

The manifest remains the package inventory; its file order is not assumed to
be dependency order. `-AlreadyAvailable` is only for an exact audited
prerequisite that will not be republished. Check each new target before its tag
is pushed:

```powershell
./eng/package-release-availability.ps1 `
  -Package nodes `
  -ExpectedState Missing
```

Do not begin a dependent wave until every package in its prerequisite waves is
indexed, restorable, and represented by the matching release and assets.

If publication succeeds but indexing or release creation fails, do not push the
package again and do not move its tag. Verify the exact public id/version, reuse
the package and symbol artifacts retained by the same workflow run, and resume
only the incomplete verification or release-record operation. If the package
version is still absent, the original publication did not complete and a normal
rerun may proceed. Any ambiguous or conflicting state is a stop condition.

## Current canonical publication evidence

The 2026-08-03 canonical train published 58 new manifest versions from one
immutable commit and reused the audited existing Mapping 1.0.3 prerequisite.
The planner emitted five dependency waves. The 25-package fourth wave was run
as independent bounded sub-batches of 8, 8, and 9 to reduce shared provider-test
pressure without changing dependency order.

Four workflows failed before publication: two unrelated timing tests and two
executions of the load-sensitive durable-input multi-owner concurrency check.
For each failure, publication and repository-release creation were skipped,
the exact version and release were proven absent, and only the unchanged tagged
workflow was rerun in isolation. No successful package was republished.

Final independent proof required:

- 58 successful release workflows on the exact publication commit;
- 58 exact tag and repository-release targets;
- one package and one symbol asset per new release;
- public presence plus isolated public-feed-only restore/load for all 59
  manifest packages; and
- executable public-only samples for Engine, Fluent Hosting, SQL-file durable
  input, and SQL-file durable output.

The executable proof performed real graph processing and durable enqueues, then
removed its project, isolated package cache, binaries, databases, and temporary
directories. Exact workflow ids and recovery evidence are retained in
`memory/292-coordinated-release-train.md` and the coordinated release goal.
