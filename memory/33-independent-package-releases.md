# Independent Package Releases

Date: 2026-05-31

## Decision

The solution remains the development unit. Individual package projects are the
release units.

Adding a new component project can change the solution, tests, and package
manifest, but it should not force the engine package or older component
packages to be republished.

## Release Shape

Each packable project owns:

- its own version in the project file
- its own package id
- its own release notes section
- its own tag prefix
- one entry in `eng/packages.json`

The release workflow now resolves one package per run and packs only that
project.

## Current Package Versions

- `FluxFlow.Engine`: `0.5.0-alpha.1`
- `FluxFlow.Components.Mqtt`: `0.1.0-alpha.1`

## Tag Convention

- `engine-v0.5.0-alpha.1`
- `components-mqtt-v0.1.0-alpha.1`

## Result

The MQTT package can be published as its first prerelease without changing the
engine package version. Future component packages should start at their own
`0.1.0-alpha.1` unless there is a package-specific reason to choose another
version.
