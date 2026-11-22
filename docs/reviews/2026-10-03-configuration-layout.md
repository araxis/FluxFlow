# Configuration folder and namespace layout

## Responsibility boundaries

ApplicationConfiguration, ResourceRegistration and CompositionConfigurationException
now live in Application, namespace FluxFlow.Configuration.Application. LinkConfiguration,
OneOrManyLinks, LinkRegistrationExtensions and LinkConditionCompiler live in its Links
subfolder and namespace. These configure connections at application level.

ComponentRegistration, ComponentConfiguration, OutputComponentConfiguration and
ConfigurationBindingExtensions now live in Components. Batching, Mapping, Routing,
Scheduling and the Source component each retain their module folders beneath Components.
DTOs and registrars remain together. These namespaces use FluxFlow.Configuration.Components
and the corresponding module suffix.

ExpressionSource remains in Expressions and JsonSource remains in Sources. They are
shared configuration values, not components. The root contains only the project file
and package README. No additional class library, solution folder, abstraction, compatibility
wrapper or runtime behavior was introduced. SDK-style project inclusion exposes the new
physical folders in Visual Studio without explicit compile entries or solution edits.

## Migration

Consumers must update imports and rebuild. Use Configuration.Application for application
setup and resource registration, Application.Links for link helpers and values, Components
for component contracts/binding, and Components.<module> for bundled adapters. Shared value
namespaces and the package name remain unchanged. Source, tests, the isolated package
consumer and current documentation are migrated. Historical review reports are retained.

## Verification

- Release solution build: zero warnings or errors.
- Full Release suite: 2,054 test executions passed across 28 test projects, zero warnings.
- Isolated Configuration package consumer: passed on .NET 8.0.31 and .NET 10.0.12.
- Public API baseline updated for the namespace migration; no old adapter/link namespace
  imports remain in production, tests or package-consumer code.
- Physical project layout and whitespace/diff checks passed.

No commit or publication is part of this change.
