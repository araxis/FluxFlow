# vNext Canonical Application Hosting

Date: 2026-07-20

## Status

The thirty-second bounded vNext milestone is implemented on local branch
`work/hosting-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone makes the flat canonical application definition the primary
Composition.Hosting lifecycle while retaining the released standalone
`CompositionDefinition` host as a compatibility surface.

## Definition Sources

- `IApplicationDefinitionSource` is the explicit complete-definition source
  contract.
- `StaticApplicationDefinitionSource` returns one immutable definition and
  honors pre-canceled load tokens.
- `ConfigurationApplicationDefinitionSource` uses Composition's canonical
  configuration loader against either the exact root or one explicit section.
  The loader continues enforcing exactly `Resources` and `Workflows`.
- Partial patches, file watching, and remote configuration transport remain
  source-layer concerns; Hosting always applies a complete definition.

## Hosted Revision Lifecycle

- `ApplicationRevisionHost` owns initial load, manual reload, direct apply,
  immutable current revision visibility, and hosted stop/disposal over the
  existing `ApplicationRevisionCoordinator`.
- Source-load failures return `ApplicationRevisionLoadResult.Error` with stable
  code `revision.source.load_failed`. With no active application the host enters
  `Degraded`; it does not terminate the surrounding .NET host.
- Planning, preparation, and activation failures remain rejected revision
  results. An existing active revision remains visible and the host stays
  `Running`.
- Caller cancellation remains cancellation. Successful activation publishes
  the new snapshot before old-candidate drain, and stop drains/disposes the
  active candidate exactly once.
- Stable states are `Empty`, `Starting`, `Running`, `Degraded`, `Stopped`, and
  `Disposed`.

## Dependency Injection Boundary

- `AddFluxFlowApplication(...)` accepts a static definition, configuration
  root/section, or custom definition source and registers one canonical hosted
  application lifecycle.
- `ApplicationHostingBuilder` explicitly registers a candidate factory and an
  optional revision event sink. It performs no assembly scanning or discovery.
- Candidate factories remain responsible for concrete resource/workflow
  providers, components, stable-port attachments, and routing preparation.
  Composition.Hosting remains Engine-independent.
- Existing provider snapshot builders continue composing service collections
  before provider creation and bridging exact external instances without
  ownership transfer.

## Versions And Compatibility

- `FluxFlow.Composition.Hosting` moved from `2.1.0` to additive `2.2.0`.
- The legacy `AddFluxFlowComposition(...)` and `ICompositionRuntimeHost`
  declarations remain available and are documented as compatibility APIs.
- Public API baseline entry 3 changed from 136 to 177 declarations for the
  additive canonical source, result, state, host, options, builder, and
  registration types.
- SDK package validation passed against `FluxFlow.Composition.Hosting` `2.1.0`.

## Verification

- Composition.Hosting tests: 45 passed, 0 warnings.
- Release tests: 94 passed, 0 warnings.
- Complete Release no-build sweep: 2,165 tests across 63 projects passed with
  0 warnings.
- Controlled Debug and Release solution builds passed with no errors; the
  affected Hosting project builds with 0 warnings.
- Release preflight and isolated local-source package dry-run passed for
  Composition.Hosting `2.2.0`.
- A package-only net8 consumer restored the packed package, ran hosted initial
  activation, applied a replacement, verified old-candidate cleanup, stopped
  the host, and printed `HOSTING_VNEXT_CONSUMER_OK`.

## Next Boundary

Canonical Designer persistence is the final implementation milestone. It must
read and write the same flat `Resources`/`Workflows` document, preserve loaded
link declaration side, create new links source-side, model nested resources and
references, render signals separately, and reuse Composition addressing and
validation without taking runtime, resource ownership, Engine, or adapter
dependencies.
