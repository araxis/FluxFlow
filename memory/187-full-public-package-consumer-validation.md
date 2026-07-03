# Full Public Package Consumer Validation

Date: 2026-07-02

## Summary

All 55 current manifest packages were validated from the public package feed
after the binary compatibility feed-alignment recovery. This was a
verification-only pass: no package source, versions, release notes, README
files, changelog entries, public API baselines, release scripts, tags, or
publishing state changed.

## Package Set

The validated manifest aliases and current versions were:

- `nodes` `1.1.2`
- `composition` `1.0.9`
- `composition-hosting` `1.0.5`
- `mapping` `1.0.2`
- `components-requestreply` `1.1.5`
- `components-http-aspnetcore` `1.0.4`
- `engine` `2.0.1`
- `components-expressions` `2.1.2`
- `components-mqtt` `4.1.3`
- `components-mqtt-composition` `1.4.0`
- `components-mqtt-mqttnet` `1.1.7`
- `components-mqtt-pulsemqtt` `2.0.7`
- `components-mapping` `3.0.1`
- `components-mapping-composition` `1.3.0`
- `components-control` `3.0.1`
- `components-control-composition` `1.3.0`
- `components-assertions` `3.0.1`
- `components-assertions-composition` `1.3.0`
- `components-sources` `3.1.1`
- `components-sources-composition` `1.4.0`
- `components-routing` `3.0.1`
- `components-routing-composition` `1.3.0`
- `components-validation` `3.0.1`
- `components-validation-composition` `1.3.0`
- `components-filesystem` `3.1.1`
- `components-filesystem-composition` `1.4.0`
- `components-observability` `3.0.1`
- `components-observability-composition` `1.3.0`
- `components-timers` `3.1.1`
- `components-timers-composition` `1.5.0`
- `components-payloads` `3.0.0`
- `components-payloads-composition` `1.3.0`
- `components-http` `3.0.1`
- `components-http-composition` `1.3.0`
- `components-serialization` `3.0.0`
- `components-serialization-composition` `1.3.0`
- `components-metrics` `3.0.3`
- `components-metrics-composition` `1.3.0`
- `components-projections` `3.0.1`
- `components-projections-composition` `1.3.0`
- `components-expectations` `3.0.1`
- `components-expectations-composition` `1.3.0`
- `components-designer` `2.16.0`
- `components-resources` `1.6.0`
- `components-secrets` `1.6.0`
- `components-configuration` `1.5.0`
- `components-journal` `2.3.5`
- `components-sessions` `3.3.2`
- `components-sessions-composition` `1.5.0`
- `components-state` `3.0.4`
- `components-state-composition` `1.3.0`
- `components-storage` `3.0.9`
- `components-storage-composition` `1.4.0`
- `components-storage-filesystem` `3.3.4`
- `components-storage-sqlfile` `3.3.4`

## Verification

- Release tests passed: `92` passed, `0` failed, `0` skipped.
- Controlled Debug solution build passed with `0` warnings and `0` errors.
- `eng/list-package-releases.ps1` enumerated `55` manifest packages.
- Public feed verification passed for all `55` current package versions with
  `eng/package-feed-verify.ps1`.
- A temporary `net8.0` consumer project outside the repository referenced all
  `55` packages directly, restored from the public package feed with
  `--no-cache`, and built in Release configuration with `0` warnings and `0`
  errors.

## Result

The full current manifest package set is published, feed-visible, binary
compatibility-validated against same-version baselines, and consumer-restorable
as a combined public package set.
