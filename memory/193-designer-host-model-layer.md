# Designer Host Model Layer

Date: 2026-07-03

## Summary

Implemented phases 1 and 2 of the Designer host layer plan
(`docs/18-designer-host-layer.md`) as a headless host-model layer in
`samples/FluxFlow.DesignerHost`, with focused tests in
`tests/FluxFlow.DesignerHost.Tests`. The layer projects a validated
`ComponentDesignMetadataCatalog` into plain host-local view models; no renderer
UI, no resource ownership, no new package, and no new Designer/Composition
public APIs.

## Changes

- Added `samples/FluxFlow.DesignerHost` (class library, `net10.0`, not
  packable, references only `FluxFlow.Components.Designer`):
  - `PaletteItemModel` / `PortModel` with display fallbacks (component type for
    the name, `General` for the category) and fixed input/output ports ordered
    by port order then name.
  - `NodeInspectorModel` / `OptionSectionModel` / `OptionEditorModel`: options
    grouped by section attribute in first-appearance order, primary options
    before advanced ones inside each section, default section `General`.
  - `OptionEditorKind` plus explicit resolution: contract-valued editor hints
    (`text`, `number`, `expression`, `json`) win; unknown or missing hints fall
    back to the option value kind (Boolean→Toggle, Enum→Select,
    Duration→Duration, Secret→Secret, ...); final fallback is Text.
  - `ResourcePickerPromptModel` projected from
    `ComponentResourcePickerHints.Create(...)` (host-owned hints only).
  - `ValidationMessageModel` as the shared shape for metadata and composition
    validation display.
  - `DesignerHostCatalog` as the single explicit projection point (no
    reflection, no discovery, no resource access).
- Added `tests/FluxFlow.DesignerHost.Tests` with 20 tests: projection rules
  with record-built metadata plus one integration pass over the real
  `TimersComponentDesignMetadataProvider`.
- Wired both projects into `FluxFlow.sln`.
- Listed the sample in `docs/README.md` (required by
  `SampleDocumentationTests`) and updated the Designer host candidate note in
  `docs/17-component-coverage-matrix.md`.

## Boundaries

- No component package source, versions, release notes, changelog entries,
  public API baselines, tags, or publishing state changed.
- The host layer is deliberately not a manifest package; promotion to `src/`
  plus `eng/packages.json` is a separate future decision once the shape is
  earned.
- Persistence mapping (host graph model ↔ `FluxFlow.Composition` definitions)
  and renderer UI remain the next bounded passes per the plan.

## Verification

- Host-model tests passed: `20` passed, `0` failed, `0` skipped.
- Release convention tests passed after adding the docs sample inventory
  entry: `92` passed, `0` failed, `0` skipped.
- Full Release solution build and no-build test suite passed with the new
  projects included.
