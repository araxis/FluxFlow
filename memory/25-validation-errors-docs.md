# Validation And Errors Docs

Date: 2026-05-31

## Summary

Added `docs/07-validation-and-errors.md` as a focused reference for the public
error surfaces in `FluxFlow.Engine`.

## Decisions

- Keep definition validation, runtime build errors, and host lifecycle errors as
  separate concepts in the docs.
- Document that `FlowApplicationHostBuildResult.Errors` only carries host-level
  failures, while runtime build failures remain under `RuntimeBuild`.
- Recommend flattening host and runtime build errors only at the application UI
  boundary.
- Keep running node streams separate from setup errors:
  `FlowError`, diagnostics, events, and state changes.
- Keep app workspace validation outside the engine, then project into
  `ApplicationDefinition` for engine validation and build.

## Verification Target

The page should stay aligned with:

- `ApplicationDefinitionValidationErrorCode`
- `ApplicationRuntimeBuildErrorCode`
- `FlowApplicationHostBuildErrorCode`
- `FlowApplicationHostBuildResult`

## Next Step

The next public reference pass should document runtime and workflow state
transitions, including host state transitions around build, start, stop, fault,
and disposal.
