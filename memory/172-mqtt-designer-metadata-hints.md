# MQTT Designer Metadata Hints

Date: 2026-06-30

## Summary

Completed the MQTT composition Designer metadata hint pass. The `mqtt.publish`
and `mqtt.trigger` metadata now carry richer option hints plus host-owned
resource key patterns for publisher, trigger source, and clock resources. The
change is descriptive metadata only.

## Changes

- Added option section, importance, and editor hints to
  `MqttComponentDesignMetadataProvider`:
  - Publishing hint for `publishTimeoutMilliseconds`.
  - Subscription hints for `topicFilter`, `qualityOfService`,
    `receiveRetainedMessages`, and `retainAsPublished`.
  - Delivery hints for `mode` and `acknowledgement`.
  - Timeout hint for `responseTimeout`.
  - Runtime hint for `boundedCapacity`.
  - Enum, boolean, and duration options omit editor hints because Designer has
    no precise editor attribute values for those option kinds.
- Added host-owned resource key patterns while preserving existing resource
  shape:
  - Required `publisher` resource: `mqtt-publisher:{name}`.
  - Required `triggerSource` resource: `mqtt-trigger-source:{name}`.
  - Optional `clock` resource: `clock:{name}`.
- Preserved publish behavior, trigger subscription behavior, topic validation,
  acknowledgement policy, request/reply handling, adapter/client ownership,
  ports, configuration binding, runtime behavior, renderer behavior, hot reload
  behavior, and engine dependency boundaries.
- Bumped `FluxFlow.Components.Mqtt.Composition` from `1.3.1` to `1.4.0`.
- Updated the package README, package release notes, top-level changelog, and
  focused metadata tests.

## Verification

- `dotnet test tests\FluxFlow.Components.Mqtt.Composition.Tests\FluxFlow.Components.Mqtt.Composition.Tests.csproj --no-restore -v minimal`
  - Passed: 10
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  - Passed: 93
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - Passed: 84
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Passed with 0 warnings and 0 errors.
- `graphify update . --force`
  - Refreshed local graph output after the memory edits.
  - `graphify-out/` remains excluded from git.

## Next

Keep any further convention or package-family Designer metadata work separately
planned, locally scoped, and locally committed.
