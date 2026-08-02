# Designer Host Layer

This page defines the Designer host boundary that consumes package-owned
metadata without moving UI, resource ownership, or runtime policy into component
packages. The metadata projection sample and canonical persistence contracts are
implemented; renderer UI remains future host work.

## Existing Inputs

A host layer can already compose the current design-time surface:

- `ComponentDesignMetadataCatalog` for validated component metadata from
  package-owned declarations paired with runtime descriptors.
- option attributes for section, importance, editor, syntax, and related
  resource hints.
- resource attributes for host-owned resource pickers, picker kind, key
  pattern, related option names, required flags, value type, and display text.
- `ComponentResourcePickerHints.Create(...)` for an ordered host-facing view of
  resource picker hints from one metadata item or a validated catalog.
- `DesignerApplicationPersistence` for canonical flat application load/save,
  editable links, resource namespaces, resource references, and runtime link
  diagnostics.
- canonical catalog projection that adds `Events`, hides legacy `Name` and
  Dataflow-specific options, and exposes an optional `processing.profile`
  resource picker.
- validation diagnostics returned on load; saves preserve exact canonical
  component and resource type names.

These contracts describe what can be shown or selected. They do not create
resources, enumerate resource instances, choose a renderer, or bind a graph to a
running host.

## Host Responsibilities

The host layer owns the app-specific model that turns metadata into a usable
editing experience:

- Build palette view models from component type, category, display name,
  summary, icon key, preferred node name, and fixed port metadata.
- Build node inspector models by grouping options by section, ordering primary
  options before advanced options, and using editor/syntax hints when a precise
  host editor exists.
- Present semantic processing profiles instead of capacities, parallelism, or
  Dataflow ordering flags. The host may expose profile creation in its resource
  catalog without embedding technical mapper values in workflow JSON.
- Provide conservative editor fallbacks for unknown or omitted editor hints,
  including boolean and duration options that currently have no contract-valued
  editor hint.
- Bind resource picker hints to a host-owned resource catalog. The host decides
  how picker kinds map to catalogs, how key patterns are displayed or searched,
  and how required or conditional resource references are validated.
- Keep resource creation, keyed service registration, lifetimes, disposal,
  secrets, credentials, and external clients owned by the host or adapter
  package that already owns those concerns.
- Surface metadata and composition validation results in host-specific status
  views without changing package metadata contracts.
- Persist graph state as component types, node names, option values, resource
  references, port links, and host layout data. Display hints remain derived
  from package metadata rather than copied as the source of truth.
- Treat each workflow object key as component identity and keep `DisplayName`
  in host UI state rather than executable component properties.
- Use `DesignerApplicationPersistence` as the single mapping to canonical
  `FluxFlow.Composition` definitions at the host boundary.

## Implementation Status

1. Add host-local view models for palette items, node inspectors, option
   editors, resource picker prompts, ports, links, and validation messages.
2. Add a catalog adapter that projects `ComponentDesignMetadataCatalog` and
   `ComponentResourcePickerHints` into those host-local models.
3. Add a resource catalog adapter that resolves picker kind and key pattern
   hints against host-owned resources without creating or disposing them.
4. Use the implemented `DesignerApplicationPersistence` mapping instead of a
   host-local definition schema.
5. Add renderer-specific UI only after the host model and mapping behavior are
   covered by focused tests.

## Non-Goals

The implemented host-model and persistence layer does not:

- add component package source changes.
- add new Composition or Engine contracts.
- add release tags or publishing work.
- add renderer UI, visual styling, canvas behavior, or localization.
- add resource catalogs, keyed resource resolution, resource ownership, or
  lifetime management to component packages.
- add hot reload, runtime lifecycle hooks, or engine dependencies.
