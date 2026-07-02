# Public Package Consumer Validation

Date: 2026-07-02

## Summary

Post-release consumer validation passed for the recently published Designer
metadata hint release train plus the two concrete MQTT adapter releases.

Validation set:

- 42 packages from `180-designer-metadata-hint-release-workflow-recovery.md`.
- 2 MQTT adapter packages from `181-mqtt-adapter-package-release.md`.

No package source, package versions, release notes, changelog, README files,
release scripts, public API baseline files, tags, releases, or package feed
state changed during this pass.

## Package Set

Designer and shared dependencies:

- `FluxFlow.Components.Designer` `2.16.0`
- `FluxFlow.Nodes` `1.1.2`
- `FluxFlow.Mapping` `1.0.2`
- `FluxFlow.Composition` `1.0.9`
- `FluxFlow.Composition.Hosting` `1.0.5`
- `FluxFlow.Components.RequestReply` `1.1.5`

Runtime component packages:

- `FluxFlow.Components.Mapping` `3.0.1`
- `FluxFlow.Components.Control` `3.0.1`
- `FluxFlow.Components.Assertions` `3.0.1`
- `FluxFlow.Components.State` `3.0.4`
- `FluxFlow.Components.Observability` `3.0.1`
- `FluxFlow.Components.Validation` `3.0.1`
- `FluxFlow.Components.Routing` `3.0.1`
- `FluxFlow.Components.Timers` `3.1.1`
- `FluxFlow.Components.Sources` `3.1.1`
- `FluxFlow.Components.Projections` `3.0.1`
- `FluxFlow.Components.Metrics` `3.0.3`
- `FluxFlow.Components.Expectations` `3.0.1`
- `FluxFlow.Components.Http` `3.0.1`
- `FluxFlow.Components.FileSystem` `3.1.1`
- `FluxFlow.Components.Storage` `3.0.9`
- `FluxFlow.Components.Sessions` `3.3.2`
- `FluxFlow.Components.Mqtt` `4.1.3`

Composition packages:

- `FluxFlow.Components.Mapping.Composition` `1.3.0`
- `FluxFlow.Components.Control.Composition` `1.3.0`
- `FluxFlow.Components.Assertions.Composition` `1.3.0`
- `FluxFlow.Components.State.Composition` `1.3.0`
- `FluxFlow.Components.Observability.Composition` `1.3.0`
- `FluxFlow.Components.Validation.Composition` `1.3.0`
- `FluxFlow.Components.Routing.Composition` `1.3.0`
- `FluxFlow.Components.Timers.Composition` `1.5.0`
- `FluxFlow.Components.Sources.Composition` `1.4.0`
- `FluxFlow.Components.Serialization.Composition` `1.3.0`
- `FluxFlow.Components.Payloads.Composition` `1.3.0`
- `FluxFlow.Components.Projections.Composition` `1.3.0`
- `FluxFlow.Components.Metrics.Composition` `1.3.0`
- `FluxFlow.Components.Expectations.Composition` `1.3.0`
- `FluxFlow.Components.Http.Composition` `1.3.0`
- `FluxFlow.Components.FileSystem.Composition` `1.4.0`
- `FluxFlow.Components.Storage.Composition` `1.4.0`
- `FluxFlow.Components.Sessions.Composition` `1.5.0`
- `FluxFlow.Components.Mqtt.Composition` `1.4.0`

MQTT adapters:

- `FluxFlow.Components.Mqtt.MqttNet` `1.1.7`
- `FluxFlow.Components.Mqtt.PulseMqtt` `2.0.7`

## Verification

Local checks:

- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  passed: 86 passed, 0 failed.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed after a scoped build-server shutdown cleared stale local output locks.

Public package-feed checks:

- `eng/package-feed-verify.ps1` passed for all 44 package IDs and versions.
- Every package was visible on the first index attempt.
- Every per-package restore/build check passed on the first verification
  attempt.

Combined consumer check:

- A temporary `net8.0` console project was created outside the repo.
- The project referenced all 44 package IDs with explicit versions.
- `dotnet restore` from the public package feed passed.
- `dotnet build --no-restore -v minimal` passed with 0 warnings and 0 errors.

## Next

The Designer metadata hint release train and current concrete MQTT adapter
packages are published, indexed, and consumer-validated. Any next source,
package-family, convention, release, or MQTT adapter work should be planned as
a separate bounded pass.
