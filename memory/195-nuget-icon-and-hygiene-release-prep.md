# NuGet Icon And Hygiene Release Prep

Date: 2026-07-03

## Summary

Added a shared NuGet package icon and prepared the composition hygiene pass for
release. All packaging was validated by dry-runs; nothing was tagged or
published. Publishing remains an operator step (see Handoff).

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

## Handoff — publishing is an operator step

- Local `main` is `448` commits ahead of `origin/main`; the branch push to
  `origin` was permission-blocked earlier this session. Release tags trigger
  the GitHub release workflows, so the code must reach `origin` first.
- Release each package in dependency-wave order with
  `eng/package-release-tag.ps1 -Package <alias> -Push` (waves:
  `nodes` -> `composition` -> `composition-hosting` -> the 19
  `components-*-composition` adapters). The helper re-runs the dry-run,
  requires a clean tree, asserts release notes exist, then creates and pushes
  `<tagPrefix>-v<version>`.
- After publish, verify with `eng/package-feed-verify.ps1` and rerun the full
  public consumer validation.

## Boundaries

- No tags created, nothing published. Only version, changelog, release-note,
  icon, and shared-props files changed. Node/adapter runtime behavior is
  unchanged apart from the earlier hygiene-pass source edits.
