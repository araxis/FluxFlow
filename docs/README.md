# FluxFlow documentation

The current branch contains Core, optional components, Configuration and C#/JSONata
expression packages. Configuration reads .NET sections and registers native DI
factories, ports and links. See [repository scope](../README.md) for module membership.

## Start here

- [Configuration package guide](../src/FluxFlow.Configuration/README.md): adapters,
  resource registration, examples and expression setup.
- [Configuration and links](02-definitions-and-links.md): flat section structure,
  addresses and link syntax.
- [Component registration](40-component-registration.md): registration, construction
  and ownership responsibilities.
- [Hosting](05-hosting-and-observability.md) and [application lifetime](08-runtime-states.md).
- [Component implementation](03-node-authoring.md) and [package implementation](04-package-authoring.md).
- [Validation and errors](07-validation-and-errors.md).
- [Configuration providers](09-json-conversion.md) and [conditional links](51-expression-conditions.md).
- [Public API overview](14-public-api-overview.md).
- [C# expressions](../src/Expressions/FluxFlow.Expressions.CSharp/README.md) and
  [JSONata expressions](../src/Expressions/FluxFlow.Expressions.Jsonata/README.md).

## Recent cleanup evidence

- [Typed port links and expression sources](reviews/2026-09-22-configuration-value-types.md).
- [Readable expression engine registration](reviews/2026-09-22-expression-engine-registration.md).
- [Core.Configuration boundary removed](reviews/2026-09-22-core-configuration-boundary.md).
- [Core boundary review and event cleanup](reviews/2026-09-22-core-boundary-and-events.md).
- [Address parts centralized](reviews/2026-09-22-address-parts.md).
- [Cycle detection simplified](reviews/2026-09-22-cycle-detection.md).
- [Deep cleanup audit and unused retry engine removal](reviews/2026-09-22-deep-cleanup-audit.md).
- [Request/reply simplification](reviews/2026-09-22-request-reply-simplification.md).
- [Unused processing profiles removed](reviews/2026-09-22-processing-profile-removal.md).
- [Direct link section parsing](reviews/2026-09-22-direct-link-sections.md).
- [Native registration cleanup](reviews/2026-09-22-native-registration-cleanup.md).
- [Intermediate document model removal](reviews/2026-09-21-intermediate-document-removal.md).
- [Performance baseline and measurement rules](43-performance-concurrency-lifetime-baseline.md):
  manual timing evidence, separate from correctness checks.

## Historical material

The numbered guides outside the current reading list include earlier designs and
removed packages. In particular, [code-first composition](12-component-composition.md),
[flat definition builders](50-flat-application-definition.md),
[workspace projection](06-workspace-projection.md) and
[revision architecture](19-vnext-runtime-architecture.md) describe previous APIs.
Use the current guides above for implementation.

[Plans](plans/) and [reviews](reviews/) preserve decisions and validation at their
recorded dates. Later removal decisions supersede earlier preservation notes.
Document definitions/serializers, hot reload, persistence packages and standalone
OpenTelemetry/SSE adapters are not part of the current minimum framework.
