# Designer Metadata Hint Release Workflow Recovery

Date: 2026-07-02

## Summary

The Designer metadata hint release workflow recovery is complete.

The tag-triggered release failures were caused by a release-test path
normalization bug in `ComponentPackageBoundaryTests`: Windows-style
`ProjectReference Include` values were combined directly on Linux runners. The
test helper now normalizes both `/` and `\` before combining project-reference
paths, matching the existing composition metadata convention-test pattern, and
a focused regression covers both separator styles.

The code/test fix commit is:

```text
31800f5b3ecb0a5985e2eb7d32be6dd2d6221f77
```

All 42 release tags from the Designer metadata hint train were retargeted from
the previous failed release target:

```text
d7da08e5bad380e243cdd49988808285292d66de
```

to the fixed commit above. The two already-present runtime dependency tags
remained skipped:

- `components-serialization-v3.0.0`
- `components-payloads-v3.0.0`

No package source, package versions, release notes, changelog, README files,
release scripts, or public API baseline files changed during this recovery.

## Published Tags

The 42 retargeted tags were pushed one at a time in the recorded dependency
order. Each tag produced a tag-push release workflow run, so no manual dispatch
fallback was needed.

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

## Verification

Local verification before retagging:

- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  passed: 86 passed, 0 failed.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed after a scoped build-server shutdown cleared a stale local assembly
  lock.

Pre-retag safety checks passed for all 42 tags:

- local and remote tags still resolved to the failed release target before
  recovery.
- no release with package assets existed for any of the 42 tag names.
- no corresponding package version was visible on the public package feed.

Release workflow recovery verification passed:

- each local tag and remote peeled tag resolves to
  `31800f5b3ecb0a5985e2eb7d32be6dd2d6221f77`.
- each tag has a successful latest tag-push release workflow run.
- each release has package assets.
- each package version is visible on the public package feed.
- `eng/package-feed-verify.ps1` passed for each package after its workflow
  succeeded.

Three workflow runs needed a single rerun after transient full-suite test
failures; each rerun passed before the train continued:

- `composition-hosting-v1.0.5` run `28566561613`.
- `components-control-v3.0.1` run `28567765211`.
- `components-sessions-composition-v1.5.0` run `28582576053`.

## Release State

The Designer metadata hint release train is now published and indexed. Future
work should be planned as a separate bounded pass; do not retarget these tags
again unless a concrete release issue requires a new recovery plan.
