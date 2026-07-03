# Designer Resource Picker Hint Package Release

Date: 2026-07-02

## Summary

Published `FluxFlow.Components.Designer` `2.17.0` from the clean release
commit that added neutral resource picker hint contracts. This was a
release-only pass: no package source, versions, README files, changelog
entries, public API baselines, release scripts, runtime behavior, or package
metadata changed.

## Release

- Package alias: `components-designer`
- Package ID: `FluxFlow.Components.Designer`
- Version: `2.17.0`
- Tag: `components-designer-v2.17.0`
- Release commit: `738f2e1cf38aaff083e6534004a7baa342020904`
- Workflow run: `28622249640`
- GitHub release: `https://github.com/araxis/FluxFlow/releases/tag/components-designer-v2.17.0`

Pre-release checks confirmed the worktree was clean at `738f2e1`, the release
tag was absent locally and on `origin`, and the public package feed did not yet
contain `FluxFlow.Components.Designer` `2.17.0` (nearest version was `2.16.0`).

## Verification

- Designer tests passed: `97` passed, `0` failed, `0` skipped.
- Release tests passed: `92` passed, `0` failed, `0` skipped.
- Controlled Release build passed with `0` warnings and `0` errors.
- Controlled Debug build passed with `0` warnings and `0` errors.
- Binary compatibility preflight passed for `components-designer` `2.17.0`
  against published baseline `2.16.0`.
- Release preflight passed and confirmed changelog coverage plus the expected
  release tag.
- Fast release dry-run passed with package archive inspection, consumer smoke,
  and feed-style verification.
- `components-designer-v2.17.0` was created and pushed from
  `738f2e1cf38aaff083e6534004a7baa342020904`.
- Tag-triggered workflow run `28622249640` completed successfully.
- Local and remote peeled tags both resolve to
  `738f2e1cf38aaff083e6534004a7baa342020904`.
- The GitHub release has two assets:
  `FluxFlow.Components.Designer.2.17.0.nupkg` and
  `FluxFlow.Components.Designer.2.17.0.snupkg`.
- Public package-feed verification passed for
  `FluxFlow.Components.Designer` `2.17.0`.

## Result

The Designer resource picker hint contract package is published, release assets
exist, and the public package feed can restore and load `2.17.0`.
