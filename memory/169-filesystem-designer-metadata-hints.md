# FileSystem Designer Metadata Hints

Date: 2026-06-30

## Summary

Completed the FileSystem composition Designer metadata hint pass. The
`file.read`, `file.write`, `directory.enumerate`, and `file.watch` metadata now
carry richer option hints plus a host-owned resource key pattern for the
optional clock resource. The change is descriptive metadata only.

## Changes

- Added option section, importance, and editor hints to
  `FileSystemComponentDesignMetadataProvider`:
  - Runtime hint for `boundedCapacity`.
  - Path hints for `baseDirectory`, `allowAbsolutePaths`, `directory`, and
    `filter`.
  - Encoding hint for `defaultEncoding`.
  - Limits hints for `maxBytes` and `maxEntries`.
  - Traversal hints for `includeSubdirectories`, `includeFiles`, and
    `includeDirectories`.
  - Watching hints for `notifyFilters` and `internalBufferSize`.
  - Boolean path/traversal options omit editor hints because Designer has no
    boolean editor attribute value.
- Added the host-owned `clock:{name}` resource key pattern while preserving the
  existing optional clock resource shape.
- Preserved path resolution, absolute-path policy, encoding fallback, file
  read/write/enumerate/watch behavior, watcher lifecycle, ports, configuration
  binding, runtime behavior, renderer behavior, hot reload behavior, and engine
  dependency boundaries.
- Bumped `FluxFlow.Components.FileSystem.Composition` from `1.3.2` to `1.4.0`.
- Updated the package README, package release notes, top-level changelog, and
  focused metadata tests.

## Verification

- `dotnet test tests\FluxFlow.Components.FileSystem.Composition.Tests\FluxFlow.Components.FileSystem.Composition.Tests.csproj --no-restore -v minimal`
  - Passed: 27
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  - Passed: 93
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - Passed: 84
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Initial run timed out with a lingering FluxFlow-owned `dotnet build`
    process.
  - After stopping that process and running `dotnet build-server shutdown`, the
    rerun passed with 0 warnings and 0 errors.
- `graphify update . --force`
  - Refreshed local graph output: 11824 nodes, 20048 edges, 1082 communities.
  - `graph.html` was skipped because the graph exceeds the local HTML
    visualization limit; `graphify-out/` remains excluded from git.

## Next

Keep any further package-family Designer metadata hint pass separately planned,
locally scoped, and locally committed. Storage is the next likely
component-family candidate if the rollout continues.
