# Validation Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Validation` as an independent component package,
starting with `json.schema-validator`.

Validation is a reusable workflow primitive after mapping and control:
mapping shapes data, control routes it, and validation checks whether selected
values satisfy a schema before a workflow continues.

## Package Boundary

The validation package owns:

- JSON schema parsing and schema file loading
- per-message schema evaluation
- result, valid, and invalid routing
- validation result and issue contracts
- stable validation error codes
- validation diagnostics
- host registration helpers and focused tests

The package does not own:

- application workspace schemas
- transport envelopes
- dashboards, activity projections, or editors
- product-specific scenario behavior

Hosts can register input type aliases and value selectors. This keeps payload
selection application-owned while allowing the package to validate plain
objects, JSON values, strings, and byte arrays.

## Implemented Node

```text
Node type: json.schema-validator
Input:     Input
Outputs:   Result, Valid, Invalid
Options:   schema, schemaPath, schemaId, inputType, valueSelector,
           payloadSelector, boundedCapacity
```

Invalid data is normal workflow output: the node emits a validation result and
routes the original input to `Invalid`.

Schema loading failures, selector failures, conversion failures, and evaluation
failures emit `FlowError`. Per-message failures continue processing later
messages where possible.

## Review Notes

- `schemaId` is package metadata and is not forced into the schema parser as a
  base URI. A schema document can still declare its own `$id`.
- `payloadSelector` is accepted as an alias for hosts migrating from
  payload-focused components, but the package-level concept is `valueSelector`.
- The first test set covers valid routing, invalid routing, schema path loading,
  missing schema, malformed schema, selector failure recovery, byte/object
  conversion, completion, and diagnostics.

## Next Steps

1. Tag and publish `components-validation-v0.1.0-alpha.1` after this commit.
2. Migrate the first consuming application by keeping its editor/catalog shape
   and delegating runtime validation to this package through a selector.
3. Consider `validation.required-fields` or a typed helper only after a second
   consumer proves the need.
