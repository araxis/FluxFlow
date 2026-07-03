# Designer Metadata Hint Publication Sequencing

Date: 2026-07-01

## Summary

Recorded a local publication-sequencing handoff for the completed Designer
metadata hint train. This pass did not publish, tag, push, merge, open a PR, or
change package source, package versions, release notes, README files, changelog
entries, public API baselines, or release scripts.

`memory/175-designer-metadata-hint-dependency-source-readiness.md` resolved the
local dry-run blocker by seeding a complete current-branch temp package source.
This note records the dependency-aware order and the release-helper commands a
later explicit release pass should use.

## Publication Order

Run release commands in this order unless a later explicit release plan changes
the package set.

### 1. Designer Contract Package

- `components-designer` `2.16.0` / `components-designer-v2.16.0`

Publish this before composition packages that consume the Designer
option/resource hint contracts.

### 2. Shared And Runtime Dependencies

- `nodes` `1.1.2` / `nodes-v1.1.2`
- `mapping` `1.0.2` / `mapping-v1.0.2`
- `composition` `1.0.9` / `composition-v1.0.9`
- `composition-hosting` `1.0.5` / `composition-hosting-v1.0.5`
- `components-requestreply` `1.1.5` /
  `components-requestreply-v1.1.5`
- `components-mapping` `3.0.1` / `components-mapping-v3.0.1`
- `components-control` `3.0.1` / `components-control-v3.0.1`
- `components-assertions` `3.0.1` / `components-assertions-v3.0.1`
- `components-state` `3.0.4` / `components-state-v3.0.4`
- `components-observability` `3.0.1` /
  `components-observability-v3.0.1`
- `components-validation` `3.0.1` / `components-validation-v3.0.1`
- `components-routing` `3.0.1` / `components-routing-v3.0.1`
- `components-timers` `3.1.1` / `components-timers-v3.1.1`
- `components-sources` `3.1.1` / `components-sources-v3.1.1`
- `components-serialization` `3.0.0` /
  `components-serialization-v3.0.0`
- `components-payloads` `3.0.0` / `components-payloads-v3.0.0`
- `components-projections` `3.0.1` / `components-projections-v3.0.1`
- `components-metrics` `3.0.3` / `components-metrics-v3.0.3`
- `components-expectations` `3.0.1` /
  `components-expectations-v3.0.1`
- `components-http` `3.0.1` / `components-http-v3.0.1`
- `components-filesystem` `3.1.1` /
  `components-filesystem-v3.1.1`
- `components-storage` `3.0.9` / `components-storage-v3.0.9`
- `components-sessions` `3.3.2` / `components-sessions-v3.3.2`
- `components-mqtt` `4.1.3` / `components-mqtt-v4.1.3`

`components-serialization-v3.0.0` and `components-payloads-v3.0.0` were already
present locally and on the configured remote during this handoff check. A later
release pass should not attempt to recreate those tags.

### 3. Metadata Hint Composition Packages

- `components-mapping-composition` `1.3.0` /
  `components-mapping-composition-v1.3.0`
- `components-control-composition` `1.3.0` /
  `components-control-composition-v1.3.0`
- `components-assertions-composition` `1.3.0` /
  `components-assertions-composition-v1.3.0`
- `components-state-composition` `1.3.0` /
  `components-state-composition-v1.3.0`
- `components-observability-composition` `1.3.0` /
  `components-observability-composition-v1.3.0`
- `components-validation-composition` `1.3.0` /
  `components-validation-composition-v1.3.0`
- `components-routing-composition` `1.3.0` /
  `components-routing-composition-v1.3.0`
- `components-timers-composition` `1.5.0` /
  `components-timers-composition-v1.5.0`
- `components-sources-composition` `1.4.0` /
  `components-sources-composition-v1.4.0`
- `components-serialization-composition` `1.3.0` /
  `components-serialization-composition-v1.3.0`
- `components-payloads-composition` `1.3.0` /
  `components-payloads-composition-v1.3.0`
- `components-projections-composition` `1.3.0` /
  `components-projections-composition-v1.3.0`
- `components-metrics-composition` `1.3.0` /
  `components-metrics-composition-v1.3.0`
- `components-expectations-composition` `1.3.0` /
  `components-expectations-composition-v1.3.0`
- `components-http-composition` `1.3.0` /
  `components-http-composition-v1.3.0`
- `components-filesystem-composition` `1.4.0` /
  `components-filesystem-composition-v1.4.0`
- `components-storage-composition` `1.4.0` /
  `components-storage-composition-v1.4.0`
- `components-sessions-composition` `1.5.0` /
  `components-sessions-composition-v1.5.0`
- `components-mqtt-composition` `1.4.0` /
  `components-mqtt-composition-v1.4.0`

## Operator Commands

For each alias and version in the order above:

```powershell
.\eng\package-release-preflight.ps1 -Package <alias> -Version <version>
.\eng\package-release-tag.ps1 -Package <alias> -Version <version> -PrepareOnly
```

Before any actual tag operation, rerun package dry-runs from a complete
current-branch package source, matching the dependency-source readiness pass:

```powershell
.\eng\package-release-dry-run.ps1 -Package <alias> -Version <version> -SkipSolutionBuild -PackageSource <seeded-source>
```

Only after an explicit release request, and only for tags that are still absent:

```powershell
.\eng\package-release-tag.ps1 -Package <alias> -Version <version>
```

Push remains a separate explicit decision:

```powershell
.\eng\package-release-tag.ps1 -Package <alias> -Version <version> -Push
```

## Verification

- `git status --short`
  - Clean before the handoff checks.
- `eng/list-package-releases.ps1`
  - Confirmed 55 package aliases and current versions/tags.
- `eng/package-release-preflight.ps1`
  - Passed for all 44 aliases in the dependency closure and composition train.
- `eng/package-release-tag.ps1 -PrepareOnly`
  - Passed for all 44 aliases.
- Local and configured-remote tag checks:
  - 42 tags were absent locally and remotely.
  - 2 tags were already present locally and remotely:
    `components-serialization-v3.0.0` and `components-payloads-v3.0.0`.
- `graphify update . --force`
  - Refreshed local graph output after the memory updates.
  - `graphify-out/` remained excluded from git.

## Next

The next pass should be an explicit release execution or handoff pass. It should
not mix publication with source changes. If release execution is requested,
rerun the dry-runs with a complete seeded source, skip already-present tags, and
create or push tags only in dependency order.
