# Package Binary Compatibility Feed Alignment Recovery

Date: 2026-07-02

## Summary

Recovered the binary compatibility baseline feed-alignment pass after the
Linux release-test fixture blocker recorded in
`185-package-binary-compat-baseline-feed-alignment-blocker.md`.

The release-test fixture now writes the fake Unix `dotnet` executable with LF
line endings and has a regression assertion that the generated shebang script
contains no carriage-return bytes. The fix was committed locally as
`a62c96888f92bde4dbe303bb15eac4c1632e8da0`
(`Fix binary compatibility release test line endings`) before retagging.

## Verification Before Retagging

- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  passed: `92` passed, `0` failed, `0` skipped.
- `dotnet build FluxFlow.sln --configuration Release --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed with `0` warnings and `0` errors.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  passed with `0` warnings and `0` errors.

## Published Baseline Versions

`components-http-aspnetcore-v1.0.4` was retargeted from
`2d24d5b076550281e070294c82cce4fedd6dece9` to
`a62c96888f92bde4dbe303bb15eac4c1632e8da0` and force-pushed. The remaining
eight tags were created at the same fixed commit.

All nine tag-triggered release workflows completed successfully, each release
has two package assets, and each package version was verified from the public
NuGet feed:

| Alias | Version | Workflow run |
| --- | --- | --- |
| `components-http-aspnetcore` | `1.0.4` | `28612879041` |
| `engine` | `2.0.1` | `28613440029` |
| `components-expressions` | `2.1.2` | `28614072121` |
| `components-resources` | `1.6.0` | `28614614703` |
| `components-secrets` | `1.6.0` | `28615119866` |
| `components-configuration` | `1.5.0` | `28615595550` |
| `components-journal` | `2.3.5` | `28616099569` |
| `components-storage-filesystem` | `3.3.4` | `28616629087` |
| `components-storage-sqlfile` | `3.3.4` | `28617131306` |

`gh run watch` hit a transient GitHub API `503` while watching
`components-configuration-v1.5.0`; direct run polling showed the workflow was
still in progress and later completed successfully. No rerun was needed.

## Binary Compatibility Result

After the nine baseline versions were indexed, `eng/list-package-releases.ps1`
enumerated `55` manifest packages and
`eng/package-binary-compat-preflight.ps1 -Package <alias> -Version <version>`
passed for all `55`.

This completes the same-version binary compatibility readiness pass against
published package baselines.

## Boundaries

No package source APIs, runtime behavior, package versions, release notes,
changelog entries, README files, public API baselines, or release scripts were
changed. The only source change was the release-test fixture newline fix.
