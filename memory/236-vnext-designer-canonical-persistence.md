# vNext Designer Canonical Persistence

Date: 2026-07-20

## Status

The thirty-third bounded vNext milestone is implemented on local branch
`work/designer-persistence-vnext`. No push, tag, publication, pull request, or
merge was performed.

This milestone makes the canonical flat `Resources`/`Workflows` application
document the Designer persistence boundary. It removes the sample host's
parallel graph schema and keeps the visual editor aligned with Composition's
addressing and link validation.

## Canonical Persistence

- `DesignerApplicationPersistence` loads and saves through
  `ApplicationDefinitionJson`; Designer does not own a second application JSON
  format.
- Editable workflows, components, nested resources, resource references, and
  links retain flat component properties and canonical `ApplicationAddress`
  values.
- Loaded links preserve whether they were declared on the source output or the
  target input. New workflow links default to source-output declarations;
  system links remain target-input declarations.
- Link diagnostics come from `ApplicationLinkCompiler`. Conditions are
  preserved, and hosts can provide the same configured compiler used by their
  runtime when condition compilation is required.
- Malformed link declarations remain raw properties and round-trip without
  destructive normalization. Syntactically valid links remain editable even
  when semantic validation reports diagnostics.
- Resource reference projections use package-owned Designer metadata and
  expose required, existence, and canonical-address information without taking
  ownership of resources.

## Host And Canvas Alignment

- `PortDesignMetadataAttributes.CreateSignalMap()` provides the typed signal
  metadata view used by hosts.
- The Designer host model separates signal inputs from typed message inputs and
  maps canonical link diagnostics to validation messages.
- The sample Designer application now persists through the package contract,
  renders signal inputs separately, preserves non-rendered links and unknown
  components, and edits one workflow without discarding resources or other
  workflows.
- The obsolete sample-only `GraphModel` and `GraphDefinitionMapper` schema was
  removed.

## Versions And Compatibility

- `FluxFlow.Components.Designer` moved from local vNext `2.18.0` to additive
  `2.19.0`.
- Public API baseline entry 43 moved from 191 to 239 declarations for the
  additive persistence models and service.
- SDK package validation passed against the latest published Designer baseline,
  `2.17.1`.

## Verification

- Designer tests: 106 passed, 0 warnings.
- Designer host tests: 24 passed, 0 warnings.
- Complete Release no-build sweep: 2,166 tests across 63 projects passed.
- The Designer application and controlled Debug and Release solution builds
  passed with no warnings or errors.
- Release tests: 94 passed, 0 warnings.
- Release preflight, isolated local-source package dry-run, and binary
  compatibility validation passed for Designer `2.19.0`.
- A package-only net8 consumer loaded, edited, serialized, and reloaded the
  canonical document and printed `DESIGNER_PERSISTENCE_API_OK`.

## Next Boundary

The vNext implementation milestones are complete. The remaining bounded work is
coordinated packaging verification: pack the complete current manifest into a
fresh local source and restore/build one package-only consumer referencing all
current package versions. This is verification-only and must not publish or tag
packages.
