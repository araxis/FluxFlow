# MQTT Adapter Package Release

Date: 2026-07-02

## Summary

The current concrete MQTT adapter package releases are complete.

Published packages:

- `FluxFlow.Components.Mqtt.MqttNet` `1.1.7`
  - tag: `components-mqtt-mqttnet-v1.1.7`
  - release workflow run: `28601027559`
- `FluxFlow.Components.Mqtt.PulseMqtt` `2.0.7`
  - tag: `components-mqtt-pulsemqtt-v2.0.7`
  - release workflow run: `28601580549`

Both tags target:

```text
9108abdf4c1aad1216163dd9ae36c4b51f9055df
```

No package source, package versions, release notes, changelog, README files,
release scripts, or public API baseline files changed during this release
pass.

## Verification

Pre-release checks:

- Worktree was clean.
- Both release tags were absent locally and on the configured remote.
- No release existed for either tag.
- Neither package version was visible on the public package feed.

Local verification passed:

- `dotnet test tests\FluxFlow.Components.Mqtt.MqttNet.Tests\FluxFlow.Components.Mqtt.MqttNet.Tests.csproj --no-restore -v minimal`
  passed: 33 passed, 0 failed.
- `dotnet test tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests\FluxFlow.Components.Mqtt.PulseMqtt.Tests.csproj --no-restore -v minimal`
  passed: 22 passed, 0 failed.
- `dotnet test tests\FluxFlow.Components.Mqtt.Tests\FluxFlow.Components.Mqtt.Tests.csproj --no-restore -v minimal`
  passed: 58 passed, 0 failed.
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  passed: 86 passed, 0 failed.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed after stopping the timed-out FluxFlow-owned build parent and shutting
  down build servers.

Release readiness passed:

- `eng/package-release-preflight.ps1` passed for both package aliases.
- `eng/package-release-dry-run.ps1 -SkipSolutionBuild` passed for both package
  aliases.
- `eng/package-release-tag.ps1 -SkipSolutionBuild -Push` created and pushed
  both tags.

Post-release verification passed:

- Local and remote tags resolve to
  `9108abdf4c1aad1216163dd9ae36c4b51f9055df`.
- Both tag-triggered release workflow runs succeeded on first attempt.
- Both releases have package and symbol package assets.
- `eng/package-feed-verify.ps1` passed for both package versions from the
  public package feed.

## Release State

The concrete MQTT adapter package updates are now published and indexed:

- `FluxFlow.Components.Mqtt.MqttNet` `1.1.7`
- `FluxFlow.Components.Mqtt.PulseMqtt` `2.0.7`

Future MQTT adapter work should start from these released versions and be
planned as a separate bounded pass.
