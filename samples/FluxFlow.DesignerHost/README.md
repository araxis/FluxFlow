# FluxFlow.DesignerHost

Headless Designer host-model layer from `docs/18-designer-host-layer.md`. It
projects a validated `ComponentDesignMetadataCatalog` into plain host-local
view models. Canonical application persistence now comes from
`DesignerApplicationPersistence` in `FluxFlow.Components.Designer`, so the host
does not maintain a second definition model.

## What It Owns

- `PaletteItemModel` / `PortModel`: component list entries with display
  fallbacks (type string for the name, `General` for the category), typed
  inputs, payload-independent signal inputs, and outputs in separate lists.
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
- `DesignerApplicationPersistence`: package-owned mapping between the canonical
  flat `ApplicationDefinition` and editable workflows, links, resource
  namespaces, and resource references. Host layout remains separate.

## What It Does Not Own

Resource catalogs, keyed service registration, resource lifetimes, renderer UI,
layout persistence, and canvas behavior stay outside this layer.

## Tests

`tests/FluxFlow.DesignerHost.Tests` covers host projection rules with
builder-made metadata and one integration pass over package-owned component
declarations.
`tests/FluxFlow.Components.Designer.Tests` covers canonical persistence,
declaration-side preservation, resource projections, and runtime diagnostics.
