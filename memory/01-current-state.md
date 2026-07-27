# Current State

Updated 2026-07-27 after the major surface reset.

## Canonical Boundary

- Application JSON has one root shape: `Resources` and `Workflows`.
- Composition `6.0.0` owns canonical definitions, addressing, link compilation,
  immutable component descriptors/catalogs, and the focused
  `IApplicationResourceRegistrar` extension boundary.
- Composition exposes complete canonical link declarations through
  `ApplicationLinkCompilationResult.Declarations` and owns their serializer.
  Designer and Engine no longer consume Composition internals through production
  friend declarations.
- Engine `7.0.0` owns `AddFluxFlow(...)`, definition sources, the single
  `FluxFlowApplication` lifecycle, transactional revisions, runtime assembly,
  stable ports, diagnostics, generations, rollback, and disposal.
- Component and resource type resolution is exact. Runtime and Designer expose
  canonical identities only; obsolete aliases are rejected.
- Component configuration uses canonical option names. The counter option is
  `predicate`; the removed `expression` name is rejected.
- Expression engines and typed context factories are host-owned keyed services
  registered directly through built-in dependency injection. There is no
  package-global resolver, registry, or registration-wrapper package.
- Every active component family owns one `*ComponentDefinition` and explicit
  `ComponentDesignDeclaration` pairs. Split identity files and metadata-provider
  discovery are gone.
- `FluxFlow.Nodes` 4.0.0 owns the retained `FluxFlow.Data` namespace. The
  standalone Data project/package and test project are removed without a
  forwarding assembly or type forwarders.

## Removed Surfaces

- The obsolete hosting compatibility package and its forwarding APIs are gone.
- Both legacy application-definition migrators and runtime legacy parsing are
  gone. Stored legacy documents require a one-time external conversion.
- Alias metadata, alias registration, normalization, and fallback lookup are
  gone from Composition, Engine, Designer, and component adapters.
- The disconnected Expressions, Resources, Secrets, Configuration, and Journal
  support packages and their tests are gone. Consumer-specific equivalents
  belong in the host or an explicit adapter.
- `IComponentDesignMetadataProvider`, `ComponentDesignMetadataModule`, 19
  family providers, and split family identity classes are gone.
- Production friend access from Composition to Designer and Engine is gone.

## Preserved Runtime Capabilities

- Canonical model serialization and validation.
- Component activation, immutable revision snapshots, transactional update and
  rollback, stable addressable ports, and deterministic ownership/disposal.
- Request/reply and bounded feedback signaling.
- Trace, causation, correlation, and timestamp propagation.
- System events, diagnostics, and semantic processing profiles.
- Exact keyed resource registration through `IApplicationResourceRegistrar`.

## Package Lines

- Composition `6.0.0`, Engine `7.0.0`, Designer `5.0.0`, and Observability
  runtime `7.0.0` carry direct breaking surface changes.
- Nodes advances once from `3.0.1` to `4.0.0` for the Data defining-assembly
  move. Data is removed rather than version-bumped.
- Composition adapters move to their next major line because their packed
  dependency closure now includes Composition `6.0.0`.
- Fluent and Fluent Hosting move to `4.0.0` for the same dependency boundary.
- The current graph has 51 affected retained packages. All affected packages
  except Nodes were already on their intended current-reset target at the task
  baseline and are not advanced twice.
- `eng/packages.json` is authoritative for the complete retained inventory.

## Documentation And Verification

- `docs/21-component-type-names.md` is the obsolete-to-canonical name map.
- `docs/23-engine-2-to-3-migration.md` is now the consolidated major-reset
  migration guide despite its historical filename.
- `eng/canonical-vnext-cleanup-ledger.json` records final dispositions.
- `memory/267-major-surface-reset.md` records implementation and verification
  evidence for this round.
- `memory/268-surface-simplification.md` records this continuation's declaration,
  package-boundary, link-ownership, and version decisions.
