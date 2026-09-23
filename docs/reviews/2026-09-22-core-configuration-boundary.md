# Remove Core.Configuration - 2026-09-22

Core's Configuration folder contained three code files, none of which read
IConfiguration. Its name no longer matched the responsibilities left after the
document-model removal. Applied the agreed split without adding another project.

| Previous location | Current location |
| --- | --- |
| Core/Configuration/CompositionConfigurationException.cs | FluxFlow.Configuration/CompositionConfigurationException.cs |
| Core/Configuration/DependencyInjection/FluxFlowServiceCollectionExtensions.cs | Core/Application/ResourceRegistrationExtensions.cs |
| Core/Configuration/Resources/FlowResourceExtensions.cs | Core/Application/FlowResourceExtensions.cs |

CompositionConfigurationException is thrown by the configuration reader, so it now
belongs to that namespace and assembly. The resource helpers remain in Core and use
FluxFlow.Core.Application alongside component/port registration. They register and
resolve native keyed services, independently of configuration providers. The new
registration class name states its actual purpose.

Resource ownership is unchanged: factories use native DI-owned singleton instances;
externally supplied instances keep standard external ownership. Address validation,
relative resource resolution, explicit host-key fallback and lazy construction are
unchanged. There is no wrapper, compatibility namespace, new service provider or new
project dependency. Core now contains 47 production C# files, down from 48; the one
file moved to Configuration was not a removed feature.

Updated source/test/package-consumer imports, current package guides and API migration
notes. Removed the old folder README and empty directories. SDK file inclusion means
Visual Studio follows the physical folders without explicit solution-file edits.
Historical reports and the negative test for removed ComponentActivationContext keep
their historical names intentionally.

## Migration

- Replace resource helper imports from Core.Configuration.DependencyInjection and
  Core.Configuration.Resources with FluxFlow.Core.Application.
- Direct static callers use ResourceRegistrationExtensions instead of
  FluxFlowServiceCollectionExtensions. Extension method names/signatures are unchanged.
- Import FluxFlow.Configuration to catch CompositionConfigurationException, and
  reference that package. The exception constructor and behavior are unchanged.
- Rebuild consumers: the namespace/class/assembly changes are intentionally breaking
  development-preview API changes. No type forwarding or deprecated facade remains.

## Validation

The first full Release run passed all behavioral tests but exposed an obsolete
release test that scanned the deleted directory for InternalsVisibleTo. Replaced
that scan with a check of the compiled Core assembly's actual friend declarations,
preserving the rule that Configuration cannot access Core internals. Removed its
now-unused source-reading helper. The source API baseline was updated for the agreed
type/namespace moves.

All **1,782 test cases pass** after the Release suite rerun (103/103); the other
1,679 cases passed in the full solution run. Isolated Core and Configuration NuGet
consumers both passed on .NET 8 and .NET 10.

Evidence is under artifacts/cleanup-review-20260920: resource-boundary-api.log,
resource-boundary-tests.log, resource-boundary-release-tests.log,
resource-boundary-core-package.log and resource-boundary-configuration-package.log.
No commit/publication.
