# Serialization Designer Metadata Hints

Date: 2026-06-30

## Summary

Completed the Serialization composition Designer metadata hint pass. JSON Parse,
JSON Stringify, Text Encode, Text Decode, Base64 Encode, and Base64 Decode
metadata now carry richer shared option hints plus a host-owned clock resource
key pattern. The change is descriptive metadata only.

## Changes

- Added option section, importance, and editor hints to the shared
  `SerializationNodeOptions` metadata surface:
  - Runtime hints for `boundedCapacity`, `maxInputBytes`, and
    `maxOutputBytes`.
  - Encoding hint for `defaultEncoding`.
  - JSON hints for `writeIndented`, `allowTrailingCommas`, and
    `skipComments`.
  - Boolean JSON options omit editor hints because Designer has no boolean
    editor attribute value.
- Added the host-owned `clock` resource key pattern `clock:{name}` while
  preserving the existing clock picker kind and optional resource shape.
- Preserved serialization behavior, encoding fallback behavior, request/result
  contracts, ports, clock use, configuration binding, runtime behavior,
  resource ownership, renderer behavior, hot reload behavior, and engine
  dependency boundaries.
- Bumped `FluxFlow.Components.Serialization.Composition` from `1.2.1` to
  `1.3.0`.
- Updated the package README, package release notes, top-level changelog, and
  focused metadata tests.

## Verification

- `dotnet test tests\FluxFlow.Components.Serialization.Composition.Tests\FluxFlow.Components.Serialization.Composition.Tests.csproj --no-restore -v minimal`
  - Passed: 16
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
locally scoped, and locally committed.
