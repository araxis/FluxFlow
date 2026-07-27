# Surface Simplification

Date: 2026-07-27

## Outcome

This continuation removes parallel ownership from build configuration,
component declarations, data contracts, and link persistence while preserving
the exact canonical application and runtime behavior.

- Common build properties live in root `Directory.Build.props`; exact external
  versions live in `Directory.Packages.props` with central package management.
- Seven project references were removed after source, asset, and package-intent
  checks. The maintained solution now has 123 projects and 395 project-reference
  edges.
- Nineteen active composition families own one `*ComponentDefinition` with
  nested identity/schema names, descriptors, metadata, and exact
  `ComponentDesignDeclaration` pairs.
- All 20 composition adapter packages remain: 19 isolate optional integration
  dependencies, while Control Composition is an explicit migration marker.
- `FlowContent`, `FlowContentJsonConverter`, and `FlowError` compile into
  `FluxFlow.Nodes` 4.0.0 under their retained `FluxFlow.Data` namespace. The Data
  project, tests, package, and manifest entry are removed without forwarders.
- Composition alone parses, resolves, validates, projects, orders, and serializes
  canonical link declarations. Designer maps public declaration projections;
  Engine owns configuration reconstruction. Production friend access is zero.

## Removed Public And Package Surfaces

- `IComponentDesignMetadataProvider`, `ComponentDesignMetadataModule`, the 19
  family metadata-provider classes, and their DI registration overloads.
- Split family `*ComponentTypes`, `*ComponentOptions`, `*ComponentPorts`, and
  `*ComponentResources` classes where their contents moved under the family
  component definition.
- `ComponentDesignMetadataCatalog.FromProviders(...)`, replaced by
  `FromDeclarations(...)`.
- The `FluxFlow.Data` package and defining assembly; source type names are
  retained in Nodes and require dependent binaries to rebuild.
- Production `InternalsVisibleTo` relationships from Composition to Designer
  and Engine.

## Version Decision

The actual manifest/project graph has 22 directly changed retained packages,
51 packages in the affected dependency closure, and four unaffected packages.
Nodes changes from the task-baseline 3.0.1 to 4.0.0 for the defining-assembly
move. Every other affected retained package already had its intended current
major at the starting commit and remains there to avoid a second increment in
one unreleased reset. Data is removed and is not version-bumped.

## Verification At Documentation Boundary

- Nodes tests: 58 passed.
- Composition tests: 97 passed.
- Designer tests: 124 passed.
- Engine tests: 79 passed.
- Link ownership boundary tests: 2 passed.
- Public API baseline acceptance workflow: 2 passed after deliberate review.
- Serialized Debug solution build: 123 projects, zero warnings, zero errors.

The final Release suite, package preflights, consumer smoke, binary compatibility
review, and refreshed dependency graph are the remaining release gates.
