# FluxFlow.Components.Validation

Standalone JSON Schema validation over an explicit JSON boundary.

`JsonSchemaValidatorNode` accepts `JsonElement` and emits
`JsonSchemaValidationResult`. A valid or invalid document is a normal typed
outcome; schema loading, selection, or evaluation failure becomes `FlowError`
on the same Output. Input and selected values are detached from caller-owned
documents.

Schemas may be inline or path-based. `IJsonSchemaValueSelector` customizes the
JSON value selected for validation. The package does not convert arbitrary CLR
values implicitly; use a mapper or serializer before this node.

## Composition

Install `FluxFlow.Components.Validation.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
