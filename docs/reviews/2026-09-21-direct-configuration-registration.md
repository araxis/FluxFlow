# Direct configuration registration

Ordinary setup previously converted the complete IConfiguration tree to JSON and then
ApplicationDefinition, WorkflowDefinition and ComponentDefinition before dispatching
the original sections to registrars. That duplicated the configuration provider's model.

## Result

- AddFluxFlowApplicationAsync walks root/workflow/component/resource sections directly.
- Component registrars receive the original section. Their existing typed DTOs/options
  remain local to each adapter; no generic component model is created.
- ResourceRegistration moves from Core.Configuration.Resources to FluxFlow.Configuration.
  Its third parameter changes from ResourceInstanceDefinition to IConfigurationSection.
  It runs during Add and must capture settings for its deferred native factory.
- Processing profiles bind directly to their typed profile, validate enum values and
  retain host overrides. Nested application/workflow resources keep canonical addresses.
- Links are discovered in each component's registered port properties. Only those
  values are adapted to the shared parser. This is a local grammar adapter, not a
  second application graph. Bound parameters retain existing scalar inference rules.
- Engine selection snapshots the source workflow's override/application fallback at
  Add, including input-side cross-workflow declarations. Compilations remain deferred
  and shared within one provider. Build does not consult mutable configuration.
- Descriptors are staged and frozen before copying to the host. Registration failures
  leave the original service collection unchanged. No provider or component is created.

## Deliberate remaining boundary

The seven document model classes are not deleted wholesale. Public serialization,
the explicit ApplicationDefinitionConfiguration.Read operation, FlowJson.ApplyLinks
and diagnostic compilation still use them. Ordinary setup no longer requires or
constructs them. Deciding where that independent document API belongs is a separate
follow-up; this change does not silently remove round-trip serialization/diagnostics.

No new catalog, context, builder, provider or runtime lookup was introduced. This
change removes a processing stage, not every line of parsing: structural checks now
run on configuration sections, and shared link validation remains intact.

IConfiguration cannot recover JSON scalar types discarded by a provider. Type names
and component/resource strings now stay strings (including "true", "123", "001").
Link parameters retain prior conversion behavior. Source.ValuesJson still preserves
heterogeneous payload types. Missing/empty section distinctions remain limited by
the configuration provider. YAML was not separately exercised; it can participate
through the same IConfiguration contract.

## Validation

Evidence in artifacts/cleanup-review-20260920/direct-configuration-*:

- Release build: zero warnings/errors.
- Full solution: 2,004 successful test executions, zero failures.
- Configuration suite: 108 cases per target (.NET 8 and .NET 10).
- New in-memory/selected-root regression verifies numeric-looking type strings,
  original resource sections, local-first resolution, deferred construction,
  registration snapshots and repeated Build identity.
- Four invalid root/section shapes verify atomic rejection.
- Existing section-link regression additionally mutates application/workflow engine
  settings after Add to verify engine selection is captured with the link.
- Public source API baseline updated for the resource delegate change.
- Isolated Core and Configuration NuGet consumers pass on .NET 8/10.
- No commit, push or publication.
