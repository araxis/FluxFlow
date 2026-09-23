# Direct processing profiles

Consumer audit found no production calls to ComponentDefinition.BindConfiguration or
ResolveProcessing. Only six legacy binding tests used these APIs. The binder copied
document properties, inserted Name/processing defaults, serialized JSON, and deserialized
arbitrary options. Ordinary section adapters already bind their own typed configuration.

Removed ComponentConfigurationExtensions and CompositionProcessingConfiguration.
ProcessingProfileExtensions retains only the useful resolution behavior as an extension
on IServiceProvider: resource reference + component address + capabilities -> settings.
It has no document or JSON dependency, keeps native resource lookup and host mapper
overrides, and validates capabilities before calling the mapper. Combined declared
capability flags are accepted instead of being incorrectly rejected by Enum.IsDefined.

This is not an automatic processing-policy layer. An adapter opting into profiles captures
its reference during Add, resolves settings in its factory, and explicitly applies local
options over profile defaults. Missing optional references mean normal component defaults.
The removed general serializer-options overload and automatic Name/default insertion are
intentional API removals; no production adapter depended on them. Explicit document
serialization/diagnostic APIs remain unchanged. No custom context, catalog or container added.

Validation:
- Full Release suite: 2,000 successful executions, no failures or compiler warnings/errors
  in the log (six old helper cases replaced by seven focused cases per target).
- New cases exercise all buffer mappings, sequential defaults, both parallel order modes,
  combined capability flags, unsupported-mode rejection before a custom mapper executes,
  custom mapper identity and invalid sequential/relaxed ordering.
- API baseline updated; current migration documentation updated.
- Real isolated Core package consumer calls the new resolver on .NET 8 and .NET 10.
- Evidence: artifacts/cleanup-review-20260920/profile-binding-{tests,api,consumer}.log.
- No commit, push or publication.

Next review candidate: document selection still retained inside ApplicationConditionCompiler.
Separating that from shared compilation may remove another document dependency without
removing diagnostic or explicit document functionality.
