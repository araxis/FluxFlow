# HTTP Designer Metadata Hints

Date: 2026-06-30

## Summary

Completed the HTTP composition Designer metadata hint pass.
`http.client` metadata now carries richer option hints plus host-owned resource
key patterns for the required client and optional clock resources. The change is
descriptive metadata only.

## Changes

- Added option section, importance, and editor hints to
  `HttpComponentDesignMetadataProvider`:
  - Limits hint for `maxResponseBodyBytes`.
  - Runtime hints for `boundedCapacity` and `maxDegreeOfParallelism`.
  - Timeouts hint for `defaultTimeoutMilliseconds`.
  - Response hint for `treatNonSuccessStatusAsError`.
  - The boolean response option omits an editor hint because Designer has no
    boolean editor attribute value.
- Added host-owned resource key patterns while preserving existing resource
  shape:
  - Required `client` resource: `http-client:{name}`.
  - Optional `clock` resource: `clock:{name}`.
- Preserved HTTP sending, `HttpClient` ownership, timeout behavior,
  response/error routing, body truncation, concurrency, ports, configuration
  binding, runtime behavior, renderer behavior, hot reload behavior, and engine
  dependency boundaries.
- Bumped `FluxFlow.Components.Http.Composition` from `1.2.2` to `1.3.0`.
- Updated the package README, package release notes, top-level changelog, and
  focused metadata tests.

## Verification

- `dotnet test tests\FluxFlow.Components.Http.Composition.Tests\FluxFlow.Components.Http.Composition.Tests.csproj --no-restore -v minimal`
  - Passed: 14
- `dotnet test tests\FluxFlow.Components.Designer.Tests\FluxFlow.Components.Designer.Tests.csproj --no-restore -v minimal`
  - Passed: 93
- `dotnet test tests\FluxFlow.Release.Tests\FluxFlow.Release.Tests.csproj --no-restore -v minimal`
  - Passed: 84
- `dotnet build FluxFlow.sln --no-restore --disable-build-servers /m:1 /nodeReuse:false -p:UseSharedCompilation=false -clp:ErrorsOnly`
  - Passed with 0 warnings and 0 errors.
- Local graph output was refreshed with `graphify update . --force` during
  closeout and remains excluded from git.

## Next

Keep any further package-family Designer metadata hint pass separately planned,
locally scoped, and locally committed. FileSystem is the next likely
component-family candidate if the rollout continues.
