# FluxFlow.Components.Validation.Composition

Optional `json.validate` registration and Designer metadata.

The descriptor exposes one `JsonElement` Input, one
`JsonSchemaValidationResult` Output, and Events. Valid/invalid results and
`FlowError` values share Output and are separated with conditions. There are no
Valid, Invalid, or Errors ports.

Inline schema, schema path/id, selector, type, and runtime options remain flat.
The optional selector and clock are host-owned keyed resources with Designer
picker hints.

## Registration And Design Metadata

Register components with `RegisterJsonSchemaValidator`. `ValidationComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
