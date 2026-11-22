# FluxFlow documentation

The solution is [Code/FluxFlow.sln](../Code/FluxFlow.sln). Build, test and sample
commands in these guides run from `Code` unless stated otherwise.
See [Studio setup](../Code/src/Studio/README.md) for the shared designer and web host.
See [release operations](release-operations.md) for the existing package release commands.
See [Studio release check](studio-release-check.md) for the repeatable .NET 11 host check and manual desktop checklist.

Logging: [structured requests and dedicated OTLP destinations](../Code/src/Plugins/FluxFlow.Plugins.Logging/Registration/README.md).
Implementation and verification: [Logger review](reviews/2026-10-04-logging-component.md).

HTTP communication: [outbound client setup](../Code/src/Plugins/FluxFlow.Plugins.Http/README.md).

MQTT communication: [connection, publisher and subscriber setup](../Code/src/Plugins/FluxFlow.Plugins.Mqtt/Runtime/Configuration/README.md).

Serial communication: [workflow-local port configuration](../Code/src/Plugins/FluxFlow.Plugins.Serial/Runtime/Configuration/README.md)
and [SerialText framing, lifecycle and failure behavior](../Code/src/Plugins/FluxFlow.Plugins.Serial/Runtime/README.md).

Data generation: [JSONata and Bogus setup](../Code/src/Plugins/FluxFlow.Plugins.Sources/Registration/DataGenerator.md).
Implementation and verification: [DataGenerator review](reviews/2026-10-04-data-generator.md).

ROS 2 topic components: [setup guide](../Code/src/Plugins/FluxFlow.Plugins.Ros2/README.md)
and [implementation review](reviews/2026-10-04-ros2-topics.md).

Latest configuration coverage: [setup for every current component](reviews/2026-10-04-complete-component-setup.md).

Latest component review: [existing components aligned with Core](reviews/2026-10-04-component-alignment.md).

Latest node lifecycle and authoring contract: [explicit component composition](reviews/2026-10-04-explicit-component-composition.md).

Latest cross-layer review: [cleanup findings and retained responsibilities](reviews/2026-10-03-cross-layer-cleanup.md).

Earlier authoring review: [one optional FlowNode base](reviews/2026-10-03-single-node-base.md).

Latest event contract: [typed event values and exporter-independent routing](reviews/2026-10-03-typed-event-values.md).

Latest Application cleanup: [API organization and file/dependency audit](reviews/2026-10-03-application-api-organization.md).

Latest Application address cleanup: [reuse validated address segments](reviews/2026-10-03-application-address-cleanup.md).

Latest Application event cleanup: [shared bridge completion and preserved failures](reviews/2026-10-03-event-bridge-completion.md).

Latest Application completion cleanup: [execution preparation and native port completion](reviews/2026-10-03-application-completion-setup.md).

Latest Application registration cleanup: [native node factories and shared port registration](reviews/2026-10-03-component-registration-cleanup.md).

Latest Application follow-up: [startup boundaries and shared failure handling](reviews/2026-10-03-application-startup-and-failures.md).

Latest Application cleanup: [lifecycle and direct link registration](reviews/2026-10-03-application-lifecycle-and-links.md).

Latest event cleanup: [event projection and numeric precision](reviews/2026-10-03-event-projection-cleanup.md).

Latest lifecycle cleanup: [source phases and processing cleanup](reviews/2026-10-03-core-lifecycle-cleanup.md).

Latest node cleanup: [shared native ordinary-input processing](reviews/2026-10-03-core-ordinary-inputs.md).

Latest Core cleanup: [native completion and error forwarding](reviews/2026-10-03-core-native-completion.md).

Current boundary: [Application extracted from Core](reviews/2026-10-03-application-extraction.md).

The current branch contains Core, optional components, Configuration and C#/JSONata
expression packages. Configuration reads .NET sections and registers native DI
factories, ports and links. See [repository scope](../README.md) for module membership.

## Start here

- [Getting started](01-getting-started.md): DI registration and links to runnable examples.
- [Configuration-driven console host](../Code/samples/Hosting/FluxFlow.Hosting.Sample/README.md):
  a runnable example of discovery, live output/events, detach/reattach and shutdown.
- [Configuration-driven HTTP host](../Code/samples/Hosting/FluxFlow.Hosting.Http.Sample/README.md):
  HttpTrigger and JSONata Mapper with correlated HTTP replies in a standard ASP.NET Core host.
- [MQTT-to-HTTP telemetry](../Code/samples/Hosting/FluxFlow.Hosting.Telemetry.Sample/README.md):
  a configured subscriber, JSONata mapping and HTTP client with native host lifecycle.
- [SerialText-to-HTTP telemetry](../Code/samples/Hosting/FluxFlow.Hosting.Telemetry.Sample/README.md#serial-text-variant):
  the same telemetry host's optional `SerialTelemetry` configuration.
- [HTTP-to-serial commands](../Code/samples/Hosting/FluxFlow.Hosting.Http.Sample/README.md#http-to-serial-commands):
  the HTTP host's optional `SerialCommands` configuration; 202 acknowledges admission only.
  Serial hardware and driver checks remain pending; automated serial checks use simulated I/O.
- [Configuration package guide](../Code/src/FluxFlow.Configuration/README.md): adapters,
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
- [C# expressions](../Code/src/Expressions/FluxFlow.Expressions.CSharp/README.md) and
  [JSONata expressions](../Code/src/Expressions/FluxFlow.Expressions.Jsonata/README.md).

## Recent cleanup evidence

- [Direct message construction](reviews/2026-10-03-message-construction.md):
  no temporary typed envelopes, shared identity-preserving copies and unchanged wire format.

- [Core event and envelope cleanup](reviews/2026-10-03-core-event-and-envelope-cleanup.md):
  native event mapping, retained typed addresses and shared envelope reading.

- [Native Dataflow Core](reviews/2026-10-03-native-dataflow-core.md):
  broadcast publication, native links, typed registration and retained lifecycle boundaries.

- [Reusable single-link configuration values](reviews/2026-10-03-single-link-values.md):
  one typed reader for single and collection properties, with direct registration helpers.

- [Conditional-link compilation through DI](reviews/2026-10-03-condition-compilation-di.md):
  removal of the compiler lookup class while preserving compile and failure reuse.

- [Configuration folder and namespace layout](reviews/2026-10-03-configuration-layout.md):
  application/link setup separated from component adapters within the same package.

- [Synchronous configuration setup](reviews/2026-10-03-synchronous-configuration.md):
  synchronous registration, required-section binding and review of remaining helpers.

- [DI workflows and shared port configuration](reviews/2026-10-03-di-workflows-and-shared-links.md):
  native workflow discovery, removal of duplicate lookups, shared registrar links and migration.
- [Composition and message context](reviews/2026-10-03-composition-and-context.md):
  single-pass port resolution, retained addresses, fewer variable allocations and
  remaining DI/configuration simplification candidates.
- [Single-link registration](reviews/2026-09-23-single-link-registration.md):
  direct typed source/target connection sharing the existing registration path.
- [Typed link addresses](reviews/2026-09-23-typed-link-addresses.md):
  validated component/port values, scoped binding and typed link registration.
- [Links owned by component registrars](reviews/2026-09-23-registrar-links.md):
  bound link values, no central port discovery, and configuration-only Error filtering.
- [Review before extending component setup](reviews/2026-09-23-pre-setup-audit.md):
  three corrected configuration defects, runtime boundaries and validation evidence.
- [Delivery policy removed from links](reviews/2026-09-23-link-delivery-policy-removal.md).
- [Post-filter cleanup](reviews/2026-09-23-post-filter-cleanup.md).
- [Conditional links filter unmatched messages](reviews/2026-09-23-conditional-link-filtering.md).

- [Address naming and delivery/fallback clarification](reviews/2026-09-23-link-address.md).

- [Link parameter removal and condition simplification](reviews/2026-09-23-link-condition-simplification.md).

- [Known expression extension checks](reviews/2026-09-23-expression-extensions.md).
- [Expression file paths across Mapper and Router](reviews/2026-09-23-expression-source-paths.md).
- [Optional JSON source prefixes](reviews/2026-09-23-json-source-paths.md).
- [Source samples from JSON text or files](reviews/2026-09-23-json-source.md).
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

[Plans](plans) and [reviews](reviews) preserve decisions and validation at their
recorded dates. Later removal decisions supersede earlier preservation notes.
Document definitions/serializers, hot reload, persistence packages and standalone
OpenTelemetry/SSE adapters are not part of the current minimum framework.
