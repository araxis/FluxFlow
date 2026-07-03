# NuGet Icon And Hygiene Release Prep

Date: 2026-07-03

## Summary

Added a shared NuGet package icon, prepared the composition hygiene pass for
release, and published the full 22-package release set. All 22 packages are
live and independently verified on the public NuGet feed. See Release
Execution and Post-Publish Verification below.

## Icon

- Chosen concept: "Fanout" (one source node broadcasting to three targets on an
  indigo->cyan rounded tile), matching the library's broadcast/fanout core.
  Five concepts were designed and the user selected Fanout.
- `assets/icon.svg` is the source of truth; `assets/icon.png` is a 256x256
  raster produced by a throwaway GDI+ program (System.Drawing) from matching
  draw calls.
- `Directory.Build.targets` applies `PackageIcon` and packs `assets/icon.png`
  for every project that sets an explicit `PackageId` (all shipped packages;
  test/sample projects set no `PackageId` and are untouched). The icon rides
  along in each package's next release, so all 55 packages adopt it as they
  ship.

## Version bumps in this release set

- Core (source changes from the hygiene pass, already bumped in
  `192-composition-resource-helper-relocation`): `FluxFlow.Nodes` `1.2.0`,
  `FluxFlow.Composition` `1.1.0`, `FluxFlow.Composition.Hosting` `1.1.0`.
- 19 `FluxFlow.Components.*.Composition` adapters minor-bumped for the
  `FluxFlow.Composition.Hosting` dependency removal plus the icon:
  Mqtt `1.5.0`, Mapping `1.4.0`, Control `1.4.0`, Assertions `1.4.0`,
  Sources `1.5.0`, Routing `1.4.0`, Validation `1.4.0`, FileSystem `1.5.0`,
  Observability `1.4.0`, Timers `1.6.0`, Payloads `1.4.0`, Http `1.4.0`,
  Serialization `1.4.0`, Metrics `1.4.0`, Projections `1.4.0`,
  Expectations `1.4.0`, Sessions `1.6.0`, State `1.4.0`, Storage `1.5.0`.
- CHANGELOG sections and package release notes added for all 22 packages; icon
  bullets added to the three core sections.

## Verification

- Full Release solution build passed with `0` warnings and `0` errors.
- Release convention tests passed: `92` passed, `0` failed, `0` skipped. The
  public API baseline was unchanged (version/icon changes do not touch API
  surface).
- `package-release-dry-run.ps1` passed end to end (`DRY_RUN_OK`: build, pack,
  consumer smoke, feed verify) for `nodes`, `composition`,
  `composition-hosting`, and the `components-timers-composition` adapter after
  seeding the local package source with the bumped core wave.
- Confirmed inside the packed nupkgs: `icon.png` present with
  `<icon>icon.png</icon>` in the nuspec; the Timers adapter nuspec no longer
  declares `FluxFlow.Composition.Hosting` and depends on
  `FluxFlow.Composition` `1.1.0`.
- Adapter dry-runs require the bumped dependency wave in the package source
  first (documented seeding rule from `175`-`177`).

## Release execution

- The push to the default branch `main` stayed permission-blocked, but pushing
  the feature branch `work/designer-host-model` to `origin` succeeded, so the
  release-tag commit reached the remote and the GitHub release workflows could
  run from it.
- All 22 release tags were created and pushed with
  `eng/package-release-tag.ps1 -Package <alias> -Push -SkipSolutionBuild`
  (`nodes-v1.2.0`, `composition-v1.1.0`, `composition-hosting-v1.1.0`, and the
  19 `components-*-composition` adapter tags).
- First pass: `FluxFlow.Nodes 1.2.0` published and indexed immediately. The
  other 21 workflows failed at the pre-publish "Smoke package consumer" gate
  because `FluxFlow.Nodes 1.2.0` was not yet indexed on nuget.org when they ran
  (`NU1102`) — the gate runs before the publish step, so nothing partially
  published (verified live: `composition` still showed only `1.0.9` at that
  point). This is the documented nuget.org indexing-lag flake.
- Recovery: polled the flat-container until `FluxFlow.Nodes 1.2.0` appeared
  (~6.5 min), re-ran `composition-v1.1.0` (`gh run rerun`) and it published and
  indexed (~9 min). Because the 19 adapters no longer depend on
  `FluxFlow.Composition.Hosting`, they and `composition-hosting` only needed
  `FluxFlow.Composition 1.1.0` indexed, not each other — so all 20 remaining
  workflows were re-run together and all 20 completed successfully (~9 min).

## Post-publish verification

- All 22 packages independently confirmed on the nuget.org flat-container
  index at their released versions (`FluxFlow.Nodes` `1.2.0`,
  `FluxFlow.Composition` `1.1.0`, `FluxFlow.Composition.Hosting` `1.1.0`, and
  the 19 adapters at the versions listed above).
- Embedded icon confirmed live: `GET
  v3-flatcontainer/fluxflow.nodes/1.2.0/icon` returns `200`. (The classic
  `iconUrl` registration field is `null` as expected for an embedded
  `PackageIcon` — that field only applies to the older external-URL icon
  convention.)
- Full public consumer validation: a fresh temporary `net8.0` console project
  outside the repository, referencing all 22 released packages by their new
  versions, restored from `https://api.nuget.org/v3/index.json` and built
  successfully with `0` warnings and `0` errors.

## Boundaries

- Only version, changelog, release-note, icon, and shared-props files changed.
  Node/adapter runtime behavior is unchanged apart from the earlier
  hygiene-pass source edits.
- Local `main` is still ahead of `origin/main`; only the
  `work/designer-host-model` branch (which carries this release commit) was
  pushed. Fast-forwarding `origin/main` to include this and the earlier
  session's commits remains a separate operator step.
