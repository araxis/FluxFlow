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

## Planned Verification

- Focused file-system tests.
- Full solution build and tests.
- Package pack and fresh public-feed restore/build smoke after release.
