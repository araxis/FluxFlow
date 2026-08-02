# Surface Simplification

Date: 2026-07-27

## Outcome

This continuation removes parallel ownership from build configuration,
component declarations, data contracts, and link persistence while preserving
the exact canonical application and runtime behavior.

- Common build properties live in root `Directory.Build.props`; exact external
  versions live in `Directory.Packages.props` with central package management.
- Seven project references were removed after source, asset, and package-intent
  checks. Removing the Data source/test projects then removed their incident
  references; the repository now has 122 project files and 377
  project-reference edges.
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

## Final Verification

- Nodes tests: 58 passed.
- Composition tests: 97 passed.
- Designer tests: 124 passed.
- Engine tests: 79 passed.
- Link ownership boundary tests: 2 passed.
- Public API baseline acceptance workflow: 2 passed after deliberate review.
- Restore, serialized Debug build, and serialized Release build: 123 projects,
  zero warnings, zero errors in every gate.
- Release conventions: 100 passed, zero failed, zero skipped (baseline: 98).
- Full Release suite: 1,455 passed, zero failed, zero skipped across 58 test
  projects (baseline: 1,441 across 59). The Data test project was removed, its
  tests moved intact into Nodes, and 14 new ownership/regression tests increased
  the suite rather than reducing it.
- Package release preflight: all 51 affected retained packages passed.
- Package dry-run: all 55 retained packages passed in dependency order against
  one fresh external feed. Every run included archive inspection, consumer
  smoke, and feed verification; 55 package and 55 symbol archives contained the
  expected shared assets, and no nuspec retained a Data dependency.
- Binary compatibility: 48 released baselines checked; 3 remained compatible,
  45 produced only documented higher-major API-break diagnostics, and 3 packages
  with no released baseline passed prepare-only validation. Unexpected failures:
  zero.
- Refreshed graph: 13,490 nodes and 27,792 edges (baseline: 13,426 and 27,414),
  with zero project cycles, stale removed source paths, obsolete provider/factory
  nodes, production friend targets, or isolated production types.
