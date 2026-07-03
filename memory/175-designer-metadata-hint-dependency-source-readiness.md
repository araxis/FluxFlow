# Designer Metadata Hint Dependency Source Readiness

Date: 2026-07-01

## Summary

Resolved the local release-readiness blocker for the completed Designer
metadata hint train without changing package source, package versions, release
notes, README files, changelog entries, public API baselines, or release
scripts. A fresh temp package source was seeded with every package listed in
`eng/packages.json`, then the impacted package preflights and fast dry-runs
were rerun against that source.

## Package Source

- Temp source:
  `C:\Users\meisa\AppData\Local\Temp\fluxflow-full-source-c0717f7c1b804d6bbd06f00041b30ce2`
- Seeded packages:
  - 55 `.nupkg` files.
  - 55 `.snupkg` files.
- Source contents came from the current branch after a controlled Release build
  and `dotnet pack <project> --configuration Release --no-build --output
  <temp-source>` for every `eng/packages.json` entry.

## Impacted Release Set

- `components-designer` `2.16.0` / `components-designer-v2.16.0`.
- `1.3.0` composition packages:
  - `components-mapping-composition`
  - `components-control-composition`
  - `components-assertions-composition`
  - `components-state-composition`
  - `components-observability-composition`
  - `components-validation-composition`
  - `components-routing-composition`
  - `components-serialization-composition`
  - `components-payloads-composition`
  - `components-projections-composition`
  - `components-metrics-composition`
  - `components-expectations-composition`
  - `components-http-composition`
- `1.4.0` composition packages:
  - `components-sources-composition`
  - `components-filesystem-composition`
  - `components-storage-composition`
  - `components-mqtt-composition`
- `1.5.0` composition packages:
  - `components-timers-composition`
  - `components-sessions-composition`

## Verification

- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - Passed: 85.
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  - Passed: 93.
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - First attempt timed out at the command runner limit.
  - Rerun with a longer timeout passed with 0 warnings and 0 errors.
- `dotnet build FluxFlow.sln --configuration Release --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Passed with 0 warnings and 0 errors.
- `eng/list-package-releases.ps1`
  - Confirmed 55 packages and the impacted aliases, versions, and tags.
- Manifest-wide `dotnet pack --configuration Release --no-build`
  - Seeded the temp source with all current local package versions.
- `eng/package-release-preflight.ps1`
  - Passed for all 20 impacted aliases.
- `eng/package-release-dry-run.ps1 -SkipSolutionBuild -PackageSource <temp-source>`
  - Passed for all 20 impacted aliases.
  - Consumer smoke restores and feed verification resolved current local
    FluxFlow dependencies from the seeded temp source, with the public feed
    still available for external dependencies.
- `graphify update . --force`
  - Refreshed local graph output after the memory updates.
  - `graphify-out/` remained excluded from git.

## Result

The prior composition dry-run blocker was dependency source completeness, not a
metadata implementation or package manifest issue. With a complete current
branch package source, the Designer package and every metadata-hint composition
package in the impacted set pass local release preflight and fast package
dry-run verification.

No packages were published, tagged, pushed, merged, or released. Package
publication should still be planned separately and should respect dependency
ordering, with `components-designer` available before composition packages that
consume the new Designer option/resource hint contracts.
