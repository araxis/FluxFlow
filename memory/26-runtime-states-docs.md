# Runtime States Docs

Date: 2026-05-31

## Summary

Added `docs/08-runtime-states.md` as a focused reference for host, application
runtime, and workflow state behavior.

## Decisions

- Document host state as a snapshot-only lifecycle wrapper.
- Document runtime and workflow states as both snapshot properties and
  transition streams.
- Call out that state streams are not event history and should be linked before
  startup when transition rows matter.
- Document cancellation separately from startup failure because cancellation
  stops without faulting already-started nodes.
- Document that faulted runtime and workflow state is preserved when completion
  finishes later.
- Keep dashboard guidance at the app boundary and avoid adding UI concepts to
  the engine.

## Verification Target

The page should stay aligned with:

- `FlowApplicationHostState`
- `ApplicationState`
- `ApplicationStateChanged`
- `WorkflowState`
- `WorkflowStateChanged`
- `FlowApplicationHost`
- `ApplicationRuntime`
- `Workflow`

## Next Step

The next public reference pass should document JSON conversion details for
`ApplicationDefinition`, node port link values, default serializer options, and
workspace projection boundaries.
