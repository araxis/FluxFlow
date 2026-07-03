# FluxFlow.DesignerHost

Headless Designer host-model layer from `docs/18-designer-host-layer.md`,
phases 1, 2, and 4. It projects a validated `ComponentDesignMetadataCatalog`
into plain host-local view models and maps an editable graph model to and from
`FluxFlow.Composition` definitions, so renderer code never reads Designer or
Composition contract types directly.

## What It Owns

- `PaletteItemModel` / `PortModel`: component list entries with display
  fallbacks (type string for the name, `General` for the category) and fixed
  input/output ports.
- `NodeInspectorModel` / `OptionSectionModel` / `OptionEditorModel`: options
  grouped by section attribute (first-appearance order), primary options before
  advanced ones, with the editor kind already resolved.
- `OptionEditorKind`: the editors this host can render. Contract-valued editor
  hints win; unknown or missing hints fall back conservatively to the option
  value kind, and anything else renders as text.
- `ResourcePickerPromptModel`: host-owned resource picker prompts projected
  from `ComponentResourcePickerHints`.
- `ValidationMessageModel` / `ValidationMessageMapper`: one shape for metadata
  errors and composition diagnostics in a status view.
- `DesignerHostCatalog`: the single projection point over the metadata catalog.
- `GraphModel` / `GraphDefinitionMapper`: an editable graph (nodes, raw JSON
  option values, resource references, links, host-only layout) mapped
  losslessly to and from `CompositionDefinition`. Layout never enters a
  definition; the host persists it separately.

## What It Does Not Own

Resource catalogs, keyed service registration, resource lifetimes, renderer UI,
and canvas behavior stay outside this layer per the plan's non-goals. Renderer
UI is the remaining pass and comes only after these models.

## Tests

`tests/FluxFlow.DesignerHost.Tests` covers the projection rules with builder-made
metadata, one integration pass over the real Timers metadata provider, and
JSON-equality round-trip tests for the definition mapping.
