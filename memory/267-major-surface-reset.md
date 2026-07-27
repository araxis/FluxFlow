# Major Surface Reset

Date: 2026-07-27

## Outcome

The repository now exposes one maintained application path. Engine owns the
hosted lifecycle, Composition owns the canonical application model and focused
resource-registration contract, and component identities are exact.

Removed in this round:

- the obsolete hosting compatibility package;
- both legacy document migrators and runtime legacy-shape parsing;
- component/resource aliases, normalization, and fallback lookup;
- the counter `expression` option alias;
- package-global expression-engine and typed context-factory registries;
- disconnected Resources, Secrets, Configuration, and Journal component
  packages and their test projects.

The canonical model, executable component runtime, request/reply paths,
resource registrar, and trace/causation/correlation propagation remain intact.

## Migration Contract

Consumers must use `AddFluxFlow(...)`, `FluxFlowApplication`, the canonical
`Resources` / `Workflows` document shape, exact component type strings, the
counter `predicate` option, and exact host-owned keyed services. Obsolete
documents are converted once outside the runtime rather than accepted by a
compatibility layer.

## Version Boundary

Directly changed packages advance major versions: Composition 6, Engine 7,
Designer 5, Expressions 3, and Observability runtime 7. Composition adapters
and the Fluent packages also advance because their packed dependency closure
crosses the new Composition major.

## Evidence

The implementation includes explicit rejection tests for retired document
shapes, type names, resource names, and option names. Final release evidence is
captured by full Debug and Release builds, Release tests, public API baseline
validation, package preflight/dry-run, architecture refresh, and repository
audits.
