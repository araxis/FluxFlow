# Release 0.5.0-alpha.1

Date: 2026-05-31

## Summary

Published `FluxFlow.Engine` `0.5.0-alpha.1`.

This release adds the package-authoring registration helpers and the neutral
consumer sample:

- `FlowNodeRegistration`
- `IFlowNodeModule`
- `FlowNodeModule`
- range and module duplicate validation before registry mutation
- `samples/FluxFlow.SampleApp`

## Verification

- Local build: passed.
- Local tests: `41` passed.
- Sample app run: passed.
- Release-note extraction for `0.5.0-alpha.1`: passed.
- Local package pack: passed.
- Local package install smoke test: passed.
- Branch CI run `26718474621`: passed.
- Release workflow run `26718498700`: passed.
- GitHub Release: `v0.5.0-alpha.1`.
- Public package restore from `https://api.nuget.org/v3/index.json`: passed.

## Notes

The package registration helper is intentionally explicit and reflection-free.
Future component packages can expose either an `IFlowNodeModule` directly or a
small registry extension that creates and registers one module.

## Next Step

Rewrite the public docs around the current package boundary, using the neutral
consumer sample as the main runnable example.
