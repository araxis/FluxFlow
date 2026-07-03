# Package Binary Compatibility Baseline Feed Alignment Blocker

Date: 2026-07-02

## Summary

Started the binary compatibility baseline feed-alignment pass to publish the
nine current manifest package versions that were missing from the public feed.
The pass stopped on the first tag-triggered release workflow, before any package
artifact was published.

## Verified Before Tagging

- The tracked worktree was clean at
  `2d24d5b076550281e070294c82cce4fedd6dece9`.
- The nine planned current-version tags, GitHub releases, and package-feed
  versions were absent.
- Local release tests passed: `91` passed, `0` failed, `0` skipped.
- The controlled Release build passed with `0` warnings and `0` errors.
- The controlled Debug build passed with `0` warnings and `0` errors.

## Failed Release Attempt

`components-http-aspnetcore` `1.0.4` was the first package in the planned
dependency-safe order.

- `eng/package-release-preflight.ps1 -Package components-http-aspnetcore -Version 1.0.4`
  passed.
- `eng/package-release-dry-run.ps1 -Package components-http-aspnetcore -Version 1.0.4 -SkipSolutionBuild`
  passed.
- `eng/package-release-tag.ps1 -Package components-http-aspnetcore -Version 1.0.4 -SkipSolutionBuild -Push`
  created and pushed `components-http-aspnetcore-v1.0.4`.
- The local and remote peeled tag targets both resolve to
  `2d24d5b076550281e070294c82cce4fedd6dece9`.
- Tag workflow run `28611193314` failed in the `Test` step before `Pack`,
  release creation, package publish, and feed verification.

The failing CI test was
`PackageBinaryCompatPreflightScriptTests.Binary_compat_preflight_script_prints_success_marker_after_pack`.
On the Linux runner, the fake baseline restore command failed with:

```text
/usr/bin/env: 'bash\r': No such file or directory
```

This points to a CRLF shebang in the release-test fixture for the binary
compatibility helper. It is a release-test portability blocker, not a
package-source or package-version blocker.

## Publication State

- No GitHub release exists for `components-http-aspnetcore-v1.0.4`.
- `FluxFlow.Components.Http.AspNetCore` `1.0.4` is not visible on the public
  NuGet feed; the feed still lists only `1.0.0`.
- The remaining eight planned package tags were not created or pushed.
- The all-package binary compatibility preflight was not rerun because the
  baseline feed alignment did not complete.

## Next Step

Plan a separate recovery pass that fixes the Linux release-test fixture newline
handling, verifies release tests locally and on the tag workflow path, retargets
`components-http-aspnetcore-v1.0.4` to the fixed commit, and then resumes the
remaining baseline feed-alignment package order.

No package source, package versions, README files, changelog entries, release
notes, public API baselines, or release scripts were changed in this pass.
