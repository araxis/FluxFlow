# FileSystem Directory Enumerate Start Diagnostic

Date: 2026-06-02

`FluxFlow.Components.FileSystem` had a race in `directory.enumerate` startup
diagnostics.

## Issue

`DirectoryEnumerateNode.StartAsync(...)` started the background enumeration task
before posting `directory.enumerate.started`.

For very small directories, enumeration could emit entry/completed diagnostics
and complete the node before the started diagnostic was accepted. That made
diagnostic streams occasionally miss the startup event.

## Decision

Post `directory.enumerate.started` after path validation and before the
background enumeration task begins.

This preserves the existing node ports and contracts while making the diagnostic
sequence deterministic for fast enumerations.

## Package

- Package: `FluxFlow.Components.FileSystem`
- Version: `0.4.2-alpha.1`

## Verification

- Focused file-system tests passed: 45 tests.
- Full solution build passed in Release with 0 warnings.
- Full solution tests passed in Release.
- Package pack passed and produced
  `FluxFlow.Components.FileSystem.0.4.2-alpha.1.nupkg`.
- Release commit: `090987f`.
- Release tag: `components-filesystem-v0.4.2-alpha.1`.
- Release workflow run: `26834092306`, success.
- Main verification run: `26834080777`, success.
- Fresh public-feed restore/build smoke passed on attempt 8.
