# FileSystem Clock Hardening

Date: 2026-06-02

## Decision

Harden `FluxFlow.Components.FileSystem` with a host-provided clock for emitted
file system timestamps.

File system outputs are used by assertions, dashboards, logs, and replayable
workflow checks. The package should keep the default system behavior for
existing callers while allowing deterministic timestamps in hosts and tests.

## Package Shape

- Package: `FluxFlow.Components.FileSystem`
- Version: `0.5.0-alpha.1`
- Clock contract: `IFileSystemClock`
- Default clock: `SystemFileSystemClock`
- Registration:
  `RegisterFileSystemComponents(options => options.UseClock(clock))`

## Behavior

- `file.write` uses the configured clock for `FileWriteResult.WrittenAt`.
- `file.read` uses the configured clock for `FileReadResult.ReadAt`.
- `file.watch` uses the configured clock for `FileWatchEvent.Timestamp` and
  the matching package flow event timestamp.
- `directory.enumerate` uses the configured clock for
  `DirectoryEnumerateEntry.EnumeratedAt`.
- Existing `RegisterFileSystemComponents()` callers keep the default system
  clock.
- Existing static node `Create(context)` helpers remain available and use the
  default system clock.

## Verification

Completed local verification:

- `dotnet test tests\FluxFlow.Components.FileSystem.Tests\FluxFlow.Components.FileSystem.Tests.csproj -c Release --no-restore`
  passed with 48 tests.
- `dotnet build FluxFlow.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet test FluxFlow.sln -c Release --no-restore` passed.
- `dotnet pack src\FluxFlow.Components.FileSystem\FluxFlow.Components.FileSystem.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
  created the package and symbol package.
- Commit: `ac1b093` (`Add deterministic filesystem clock`).
- Tag: `components-filesystem-v0.5.0-alpha.1`.
- Release workflow: `26841805629`, success.
- Main CI workflow: `26841795813`, success.
- Public package restore/build smoke passed on attempt 6 after public-feed
  indexing caught up.
