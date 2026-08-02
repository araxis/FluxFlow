# T-SQL durable-input integration tests

This explicit suite validates the production T-SQL durable-input provider
against SQL Server 2022. It intentionally remains outside the main solution so
ordinary builds and tests require neither a container runtime nor network
access.

Run the suite from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/FluxFlow.Engine.DurableInput.TSql.IntegrationTests/run-integration.ps1 -AcceptLicense
```

`-AcceptLicense` confirms acceptance of the container image license. The runner
uses `mcr.microsoft.com/mssql/server:2022-latest` by default, assigns a unique
container name and host port, generates an ephemeral credential, waits at most
90 seconds for readiness, and removes the container in `finally`. It prints the
exact image tag and digest used for the run, but never prints the credential or
full connection string.

For a CI-managed server, set
`FLUXFLOW_TSQL_INTEGRATION_CONNECTION_STRING` in the process environment and
run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/FluxFlow.Engine.DurableInput.TSql.IntegrationTests/run-integration.ps1 -AcceptLicense -UseExternalConnectionString
```

The configured identity must be able to create, alter, and drop the isolated
test databases. The runner exposes the connection string only to the test
process, requires at least one executed test, and rejects skipped or failed
tests.

Use `-KeepContainer` only for an intentional diagnostic session. The retained
container name is printed, and removing it becomes the caller's responsibility.
