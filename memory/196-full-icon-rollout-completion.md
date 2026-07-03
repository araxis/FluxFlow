# Full Icon Rollout Completion

Date: 2026-07-03

## Summary

Extended the shared Fanout NuGet icon (`195-nuget-icon-and-hygiene-release-prep.md`)
to every remaining manifest package. The 22-package composition hygiene release
already carried the icon; this pass patch-bumped and republished the other 33
packages so all 55 current manifest packages carry the icon. Prompted by the
user spotting generic placeholder icons on several packages in the NuGet
gallery (Storage.FileSystem, Storage.SqlFile, Timers, Validation, Engine,
Mapping, etc.) — those were published before `Directory.Build.targets` existed,
and icon wiring only applies to a package's next pack, not retroactively.

## Version bumps

Patch-only (icon is packaging metadata; no source, API, dependency, or
behavior change) across three dependency waves:

- **Wave 1** (17, only depend on already-published `FluxFlow.Nodes`):
  Designer `2.17.1`, FileSystem `3.1.2`, Http `3.0.2`, Journal `2.3.6`,
  Metrics `3.0.4`, Payloads `3.0.1`, Projections `3.0.2`, RequestReply `1.1.6`,
  Resources `1.6.1`, Secrets `1.6.1`, Serialization `3.0.1`, Sessions `3.3.3`,
  Sources `3.1.2`, Storage `3.0.10`, Timers `3.1.2`, Validation `3.0.2`,
  Mapping `1.0.3`.
- **Wave 2** (14, depend on wave-1 packages): Assertions `3.0.2`,
  Configuration `1.5.1`, Control `3.0.2`, Expectations `3.0.2`,
  Expressions `2.1.3`, Http.AspNetCore `1.0.5`, components-Mapping `3.0.2`,
  Mqtt `4.1.4`, Observability `3.0.2`, Routing `3.0.2`, State `3.0.5`,
  Storage.FileSystem `3.3.5`, Storage.SqlFile `3.3.5`, Engine `2.0.2`.
- **Wave 3** (2, depend on wave-2's Mqtt): Mqtt.MqttNet `1.1.8`,
  Mqtt.PulseMqtt `2.0.8`.

Dependency waves were derived from actual `ProjectReference` graph analysis
(not guessed), confirming a clean 17/14/2 topology with no cycles.

## Fixture test update

`Get_release_notes_writes_current_package_section_only` in
`tests/FluxFlow.Release.Tests/ReleaseScriptTests.cs` hardcoded expected content
text tied to `FluxFlow.Components.Configuration`'s previous release notes
("typed option-path authoring", "ConfigurationOptionPath"). Bumping
Configuration to `1.5.1` with a generic icon-only section shadowed that content
in the changelog. Updated the test's expected strings to match the new
icon-only section text; the structural assertion (single section returned, no
other `## ` headers) is unchanged.

## Recurring flake encountered and resolved

The release workflow runs the full solution test suite as its pre-publish
gate. `FluxFlow.Nodes.Tests.FlowMultiOutputAndSourceTests
.Source_EmitAsync_WaitsWhenBoundedOutputIsFull` is a pre-existing race
(asserts `SecondAccepted.IsCompleted.ShouldBeFalse()` immediately after
awaiting `FirstAccepted`) unrelated to any change in this session or the prior
hygiene pass — confirmed by: the file was last touched by the original
standalone-node migration commits, and 5 isolated local reruns showed 2
failures / 3 passes. Hit `components-sessions-v3.3.3` first (asked the user,
approved a one-time retry, succeeded), then hit `components-configuration`,
`components-storage-sqlfile`, and `engine` in wave 2 (~13% of runs so far).
Asked the user for a standing policy; they approved auto-retrying this exact
confirmed flake signature without further prompts. All four affected releases
passed on retry with no other test failures. This flake is now a second
known-flaky test alongside the one in record 133 and should be considered for
a deterministic fix in a future pass.

## Verification

- Full Release solution build passed with `0` warnings and `0` errors after
  the version bumps.
- Release convention tests passed: `92` passed, `0` failed, `0` skipped (after
  the fixture text update).
- Dry-run spot checks (`components-designer`, `mapping`) passed end to end
  (`DRY_RUN_OK`); confirmed `icon.png` present in both packed nupkgs.
- All 33 tags created and pushed via
  `eng/package-release-tag.ps1 -Package <alias> -Push -SkipSolutionBuild` in
  wave order, with `gh run rerun` for the 4 confirmed-flake failures.
- Post-publish, independently verified all 33 package versions on the
  nuget.org flat-container index (separate from the release workflow's own
  feed-verify step).
- Spot-checked the embedded icon endpoint (`GET
  v3-flatcontainer/<id>/<version>/icon`) returns `200` for `FluxFlow.Engine`,
  `FluxFlow.Components.Designer`, and `FluxFlow.Mapping`.
- Full public consumer validation: a fresh temporary `net8.0` console project
  outside the repository, referencing **all 55** current manifest packages by
  their released versions, restored from `https://api.nuget.org/v3/index.json`
  and built successfully with `0` warnings and `0` errors.

## Boundaries

- Only version, changelog, release-note, and one test-fixture file changed.
  No source behavior, public API, or dependency changes across any of the 33
  packages.
- `origin/main` is still behind local `main`; only the
  `work/designer-host-model` branch (carrying this and the earlier release
  commits) was pushed. Fast-forwarding `origin/main` remains a separate
  operator step.
- The `Source_EmitAsync_WaitsWhenBoundedOutputIsFull` race was not fixed in
  this pass — only worked around by retry, per explicit user decision to keep
  the release moving. A deterministic fix is future work.
