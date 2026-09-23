# Processing-profile removal and adjacent cleanup

## Findings and removal

No production component adapter called ResolveProcessing. The profile resource could
be registered, but only profile tests and the Core package consumer exercised its
resolver. The layer invented mode/order/buffer categories and translated them to
numeric execution settings through a mapper interface and capability flags.

Removed seven Core production files: CompositionProcessingProfile (and its enums),
CompositionProcessingCapabilities, CompositionProcessingSettings,
CompositionProcessingResourceTypes, ICompositionProcessingProfileMapper,
DefaultCompositionProcessingProfileMapper and ProcessingProfileExtensions. Removed
the empty Processing directory, automatic registration in Configuration, the dedicated
profile test file and the profile section of the isolated Core consumer. This is an
intentional public API removal; external callers must migrate to their own component
settings/native Dataflow options. No current component behavior was removed.

ResourceBoundaryTests now use a resource-specific ConnectionConfiguration DTO rather
than a built-in profile. Grouped local/application resource lookup, DTO binding,
invalid-type/settings atomicity, deferred factories, registration timing and provider
ownership remain covered. No replacement production DTO/base/mapper was introduced.

## Adjacent audit

- Removed Configuration/CompatibilitySuppressions.xml: it targeted the former
  FluxFlow.Composition assembly and was not referenced by current build/validation
  configuration. Current package validation remains intact.
- Removed direct Core references to Microsoft.Extensions.Logging.Abstractions and
  Microsoft.Extensions.Options: Core source uses neither API. Hosting.Abstractions
  remains; its ordinary transitive dependencies are not claimed to disappear.
- Retained FlowResourceExtensions: actual factories use relative/full resource lookup,
  delegated to ApplicationAddress and native keyed DI, with no custom instance cache.
- Retained resource registration extensions: native DI ownership and resource-address
  validation, exercised by both configuration and native consumers.
- Retained ResourceRegistration: the active per-type section callback contract.
- Retained ExpressionSource and link parameter/compiler helpers: Mapper/Router and
  conditional links consume them for requested file/inline expressions, bound values
  and per-provider compilation reuse. They are not profile-processing infrastructure.

Core.Configuration now contains three C# files (resource registration, resource lookup,
and configuration exception), plus its README. Current guides and API baseline were
updated; dated reports remain historical. Added an explicit historical note to the
old component-type guide, which still describes removed resource types.

## Verification

Full solution Release suite: 1,804 passing executions on .NET 8/10, down by fourteen
profile-only executions. Configuration retains 142 passing cases per target. Public
API baseline updated and checked by the full suite. Whitespace diff check passes.
Both isolated Core and Configuration NuGet consumers passed on .NET 8 and .NET 10.

Evidence under artifacts/cleanup-review-20260920:

- processing-removal-tests.log
- processing-removal-api.log
- processing-removal-core-package.log
- processing-removal-configuration-package.log

No commit/publication. Future cleanup should continue examining actual consumers and
required behavior, not preserve abstractions because their own tests still use them.
