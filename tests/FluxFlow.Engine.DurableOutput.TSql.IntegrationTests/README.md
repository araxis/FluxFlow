# T-SQL durable-output integration tests

This explicit suite validates the production T-SQL durable-output provider against SQL Server 2022. It is intentionally outside the main solution so ordinary builds and tests require neither Docker nor network access.

Run the suite from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/FluxFlow.Engine.DurableOutput.TSql.IntegrationTests/run-integration.ps1 -AcceptLicense
```

`-AcceptLicense` is required and confirms acceptance of the container image license. The runner uses `mcr.microsoft.com/mssql/server:2022-latest` by default, assigns a unique container name and host port, generates a strong ephemeral password, waits at most 90 seconds for readiness, and removes the container in `finally`. It prints the exact image tag and digest used for the run, but never prints the password or full connection string.

Use `-KeepContainer` only for an intentional diagnostic session. The runner prints the retained container name; removal then becomes the caller's responsibility.

For a CI-managed server, set `FLUXFLOW_TSQL_INTEGRATION_CONNECTION_STRING` in the process environment and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/FluxFlow.Engine.DurableOutput.TSql.IntegrationTests/run-integration.ps1 -AcceptLicense -UseExternalConnectionString
```

The configured identity must be able to create, alter, and drop the isolated test databases. The runner exposes the connection string only through the test process environment, requires at least one executed test, and fails when any test fails or is skipped.
