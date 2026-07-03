# FluxFlow.DesignerHost

Headless Designer host-model layer from `docs/18-designer-host-layer.md`,
phases 1 and 2. It projects a validated `ComponentDesignMetadataCatalog` into
plain host-local view models so renderer code never reads Designer contract
types directly.

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
- `ValidationMessageModel`: one shape for metadata and composition validation
  results in a status view.
- `DesignerHostCatalog`: the single projection point over the metadata catalog.

## What It Does Not Own

Resource catalogs, keyed service registration, resource lifetimes, renderer UI,
canvas behavior, persistence, and runtime mapping stay outside this layer per
the plan's non-goals. Persistence mapping to `FluxFlow.Composition` definitions
is the next bounded pass.

## Tests

`tests/FluxFlow.DesignerHost.Tests` covers the projection rules with builder-made
metadata plus one integration pass over the real Timers metadata provider.
