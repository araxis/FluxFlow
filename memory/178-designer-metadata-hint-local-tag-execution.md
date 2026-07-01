# Designer Metadata Hint Local Tag Execution

Date: 2026-07-01

## Summary

The Designer metadata hint release train now has local annotated release tags
for the absent dependency-ordered aliases. This pass created local tags only:
no tags were pushed, no packages were published, and no package source,
release script, changelog, README, version, or public API baseline files were
changed.

The release target commit before this memory closeout was:

```text
d7da08e5bad380e243cdd49988808285292d66de
```

All newly-created local tags point to that commit. This memory-only commit is
intentionally after the release target and should not be part of the package
tag targets.

## Verification And Inputs

- The worktree was clean before tagging.
- `eng/list-package-releases.ps1` reported 55 packages and matched the
  expected aliases, versions, and tag names.
- Controlled Release build passed:

```powershell
dotnet build FluxFlow.sln --configuration Release --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly
```

- A fresh temp package source was seeded outside the repo:

```text
C:\Users\meisa\AppData\Local\Temp\fluxflow-local-tag-source-49962622e7c443909c6f9b151b53d7cf
```

- Package seeding produced 55 package files and 55 symbol package files.
- Tag helper logs were written outside the repo:

```text
C:\Users\meisa\AppData\Local\Temp\fluxflow-local-tag-logs-8ece505d32db4a6ab5dc0e4a7f09b32d
```

- Each created tag ran through `eng/package-release-tag.ps1` with
  `-SkipSolutionBuild` and the seeded temp package source.
- Each created tag was checked with `git rev-list -n 1` and points to the
  recorded release target commit.

## Created Local Tags

Created count: 42.

Designer and shared dependencies:

- `components-designer-v2.16.0`
- `nodes-v1.1.2`
- `mapping-v1.0.2`
- `composition-v1.0.9`
- `composition-hosting-v1.0.5`
- `components-requestreply-v1.1.5`

Runtime component packages:

- `components-mapping-v3.0.1`
- `components-control-v3.0.1`
- `components-assertions-v3.0.1`
- `components-state-v3.0.4`
- `components-observability-v3.0.1`
- `components-validation-v3.0.1`
- `components-routing-v3.0.1`
- `components-timers-v3.1.1`
- `components-sources-v3.1.1`
- `components-projections-v3.0.1`
- `components-metrics-v3.0.3`
- `components-expectations-v3.0.1`
- `components-http-v3.0.1`
- `components-filesystem-v3.1.1`
- `components-storage-v3.0.9`
- `components-sessions-v3.3.2`
- `components-mqtt-v4.1.3`

Composition packages:

- `components-mapping-composition-v1.3.0`
- `components-control-composition-v1.3.0`
- `components-assertions-composition-v1.3.0`
- `components-state-composition-v1.3.0`
- `components-observability-composition-v1.3.0`
- `components-validation-composition-v1.3.0`
- `components-routing-composition-v1.3.0`
- `components-timers-composition-v1.5.0`
- `components-sources-composition-v1.4.0`
- `components-serialization-composition-v1.3.0`
- `components-payloads-composition-v1.3.0`
- `components-projections-composition-v1.3.0`
- `components-metrics-composition-v1.3.0`
- `components-expectations-composition-v1.3.0`
- `components-http-composition-v1.3.0`
- `components-filesystem-composition-v1.4.0`
- `components-storage-composition-v1.4.0`
- `components-sessions-composition-v1.5.0`
- `components-mqtt-composition-v1.4.0`

## Skipped Existing Tags

Skipped count: 2.

These tags already existed locally and on the configured remote before this
pass, so they were not recreated:

- `components-serialization-v3.0.0`
- `components-payloads-v3.0.0`

## Release Sequencing Note

The next release execution step, if explicitly requested, is to push the 42
local tags in the recorded dependency order while continuing to skip the two
already-present tags. Package publication remains separate from this local tag
execution record.
