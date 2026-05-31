# JSON Conversion Docs

Date: 2026-05-31

## Summary

Added `docs/09-json-conversion.md` as a focused reference for engine JSON
serialization, definition shape, link JSON parsing, address rules, node
configuration, and workspace projection.

## Decisions

- Document `ApplicationDefinitionJson.CreateSerializerOptions()` as the
  supported entrypoint for engine definition JSON.
- Document that workflows serialize as direct node maps without a `nodes`
  wrapper.
- Document that `NodeDefinition.Ports` is JSON extension data and therefore port
  names should not collide with reserved node properties.
- Document the difference between node port link parsing and direct
  `LinkDefinition` conversion: short addresses need workflow context.
- Keep app workspace loading outside the engine and recommend explicit
  projection into `ApplicationDefinition`.

## Verification Target

The page should stay aligned with:

- `ApplicationDefinitionJson`
- `ApplicationDefinition`
- `WorkflowDefinition`
- `NodeDefinition`
- `LinkJson`
- `LinkDefinition`
- `PortAddress`

## Next Step

Keep future JSON changes covered by tests before documenting new persisted
shapes.
