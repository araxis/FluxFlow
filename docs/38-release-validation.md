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
