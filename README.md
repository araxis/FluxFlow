# FluxFlow — framework-only component redesign

The complete previous implementation is preserved on local `main` at `6a659218`.
This branch is `codex/core-only-component-redesign`.

All builtin component packages and their registration extensions/adapters have
been removed. The old component tests, samples and templates are also removed.
We will design and add components individually, starting with an agreed behavior
and reviewing implementation and registration together.

The framework remains: application/workflow graphs, ports, message/value types,
execution blocks, JSON definitions and composition, Engine, durability, hosting,
expressions, and observability. There are 19 framework/infrastructure packages;
`eng/packages.json` is the current inventory. No framework production code was
changed as part of the component reset.

## Build and verify

```powershell
dotnet restore FluxFlow.sln
dotnet build FluxFlow.sln -c Release --no-restore
./eng/test.ps1 -Suite Unit -Configuration Release -NoBuild
```

The local unit suite excludes external/licensed database integration. Package
acceptance remains available through `eng/package-consumer-acceptance.ps1`.
The removed MQTT/HTTP acceptance and smoke suites are not run on this branch.

## Next step

Choose the first component and agree on its behavior before implementation.
See [the component redesign plan](docs/plans/2026-09-14-component-redesign.md).

Existing component guides and review ledgers document the implementation saved
on `main`; they are historical references, not available component APIs here.
Current framework references include [message data](docs/20-flow-data-contracts.md),
[Core](src/Core/FluxFlow.Core/README.md), and
[OpenTelemetry/SSE](docs/46-observability-host.md), and
[Application Health Readiness](docs/42-application-health-readiness.md).
