# Validation Canonical Consolidation

Date: 2026-07-23

## Status

The Validation family consolidation is complete on local branch
`work/canonical-vnext-cleanup`. No push, tag, publication, pull request, or
merge was performed.

This milestone supersedes the additive compatibility state recorded in
`219-vnext-validation-flowvalue.md`. Validation now has one maintained
component runtime and Composition contract.

## Canonical Contract

- `FlowValueJsonSchemaValidatorNode` accepts `FlowValue` and emits one
  `FlowResult<JsonSchemaFlowValueValidationResult>` Output.
- Valid and invalid schema evaluations are successful result kinds. Missing
  input, selector failure, and schema evaluation failure are normal error
  results; later messages continue.
- Inline and path-based schemas still compile before message processing.
- `IJsonSchemaFlowValueSelector` preserves custom nested-value selection while
  keeping selected and original values as immutable `FlowValue` instances.
- Deterministic conversion covers null, scalar numeric and text values,
  objects, arrays, binary values, temporal values, durations, and GUIDs.
- Results and Events retain stable kinds/codes, deterministic clocks,
  correlation metadata, ordered fan-out, and exact original input identity.

## Removed Compatibility Surface

- Removed `JsonSchemaValidatorNode<TInput>`,
  `IJsonSchemaValueSelector<TInput>`,
  `JsonSchemaValidationResult<TInput>`, and numeric `ValidationErrorCodes`.
- Removed generic `RegisterJsonSchemaValidator<TInput>()` and the Valid and
  Invalid Composition port constants.
- Removed the generic node's Valid, Invalid, and Errors workflow surfaces.
- Removed `JsonSchemaValidatorOptions.PayloadSelector`; definitions migrate
  that alias directly to `valueSelector`.
- Migrated Validation Composition tests from obsolete Composition hosting to
  the canonical application revision and stable-port runtime.

CLR consumers now convert values explicitly at the application boundary and
route outcomes through conditions over `FlowResult.Kind`, `IsError`, and
`Error.Code`.

## Versioning And Compatibility

- `FluxFlow.Components.Validation` moves from `4.0.0` to `5.0.0`.
- `FluxFlow.Components.Validation.Composition` moves from `2.2.0` to `3.0.0`.
- Source-declaration baseline entries changed only for manifest indexes 23 and
  24: Validation from 87 to 55 declarations and Validation Composition from 16
  to 13.
- SDK package validation against the preceding versions reports only the four
  removed runtime types, removed selector alias accessors, generic registration,
  and Valid/Invalid constants on both target frameworks. No suppression was
  generated.

## Verification

- Validation runtime tests: 11 passed with no warnings.
- Validation Composition tests: 13 passed with canonical hosting and no
  warnings.
- Core Composition tests: 145 passed.
- Composition Hosting tests: 46 passed with 11 existing migration warnings.
- Designer tests: 112 passed.
- Release tests: 96 passed.
- Controlled Debug build: succeeded with 12 warnings and no errors after one
  workspace-scoped stale build-process cleanup.
- Controlled Release build: succeeded with 75 warnings and no errors.
- A fresh temporary source outside the repository was seeded with all 58
  current manifest packages.
- Release preflight and local-source dry-run passed for both changed packages,
  including archive inspection, isolated smoke restore/build, and feed checks.
- A combined package-only `net8.0` consumer restored Validation `5.0.0` and
  Validation Composition `3.0.0`, built with warnings as errors, activated the
  canonical factory from flat `Resources`/`Workflows` JSON, and verified exact
  input identity plus one normal result Output.
- `graphify update . --force` refreshed the ignored local graph to 16,658
  nodes, 35,875 edges, and 955 communities; HTML generation was skipped at the
  configured size limit.

## Next Gate

Consolidate Assertions as a separate bounded pass. Prove expression behavior,
context construction, passed/failed result parity, original input preservation,
diagnostics, and canonical composition activation before removing its generic
component and branch/Error surfaces.
