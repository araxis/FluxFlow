# Current component development

The current authoring path is documented in these focused guides:

1. [Node authoring](03-node-authoring.md): native blocks, message lineage, errors,
   broadcast publication and component lifecycle.
2. [Package authoring](04-package-authoring.md): explicit dependencies, local options
   and separate configuration adapters.
3. [Component registration](40-component-registration.md): native keyed DI factories,
   port addresses, links and ownership.
4. [Hosting](05-hosting-and-observability.md): ordinary startup and shutdown.

## Follow an existing component: Mapper

These are the actual files used by the telemetry sample; no additional sample project
or registration infrastructure is needed.

| Responsibility | File |
| --- | --- |
| Runtime behavior and native blocks | [Mapper.cs](../Code/src/Plugins/FluxFlow.Plugins.Transformation/Runtime/FluxFlow.Components.Mapping/Mapper.cs) |
| Executable delegate and Dataflow options | [MapperOptions.cs](../Code/src/Plugins/FluxFlow.Plugins.Transformation/Runtime/FluxFlow.Components.Mapping/MapperOptions.cs) |
| Bindable settings and standard links | [MapperConfiguration.cs](../Code/src/Plugins/FluxFlow.Plugins.Transformation/Design/Settings/MapperConfiguration.cs) |
| Configuration-to-DI registration | [MapperRegistrationExtensions.cs](../Code/src/Plugins/FluxFlow.Plugins.Transformation/Registration/MapperRegistrationExtensions.cs) |
| Host registration and execution | [Telemetry Program.cs](../Code/samples/Hosting/FluxFlow.Hosting.Telemetry.Sample/Program.cs) |
| Configured components and links | [Telemetry appsettings.json](../Code/samples/Hosting/FluxFlow.Hosting.Telemetry.Sample/appsettings.json) |

Mapper's runtime project references Core only. Its constructor creates Input, Output
and Events with native Dataflow blocks, declares them to FlowLifetime, and calls
Observe after initialization. ProcessMessageAsync handles incoming errors and processing
exceptions; the mapping function returns a value and WithValue preserves message lineage.
The component has no configuration, address, host or expression-engine dependency.

The adapter lives in the Transformation plugin alongside its portable settings and runtime.
MapperConfiguration inherits the data-only ComponentConfiguration for Input, Output,
Events and Error references. Engine, Expression and OutputCapacity are its own settings.
ExpressionSource reads the expression; it is not part of MapperOptions.

The registration and execution sequence is:

1. The host calls AddMapper before AddFluxFlowApplication. AddMapper registers the
   `ComponentRegistration` delegate under the exact type key `mapper`. The host also
   registers AddJsonataExpressionEngine for the configured `jsonata` engine.
2. AddFluxFlowApplication reads the selected configuration section and invokes the
   registrar. It binds MapperConfiguration, obtains the typed ComponentAddress, validates
   settings and registers a deferred component factory. Port selectors refer to that same
   component instance. AddLinks registers the DTO's standard links, including Error as a
   filter on Output rather than a separate port.
3. AddFluxFlow connects the application to Generic Host. On host startup, composition
   resolves factories and connects links before sources start. Mapper's factory reads
   and compiles its expression once, then passes an ordinary mapping delegate through
   MapperOptions. No component is created while reading configuration.
4. Host shutdown drains accepted work; the host's DI provider disposes components and
   resources. Mapper uses FlowLifetime rather than adding its own host or shutdown loop.

In the sample, `telemetry.request` maps MQTT data into an HTTP request. Its `Output`
reference, `send.Input`, resolves within the same workflow. Both `mapper` and `jsonata`
must be registered before startup; declaring a Type or Engine in JSON does not load a
package automatically. The sample project shows the required references and copies
appsettings.json beside the executable. Its host uses AppContext.BaseDirectory as the
content root, so running from another directory still finds that file.

For another component, follow these existing boundaries: runtime behavior and options
in its component project; a DTO and registrar in the appropriate configuration module;
explicit registration in the host. Keep physical folders and Visual Studio solution
folders aligned. Use individual link helpers only for ports that differ from the
standard names.

The existing [Mapping consumer](../Code/eng/mapping-consumer/Program.cs) demonstrates direct
use without Application or Configuration. [Mapper configuration tests](../Code/tests/Unit/FluxFlow.Configuration.Tests/MapperConfigurationTests.cs)
cover deferred construction, invalid expressions and message lineage; the telemetry
tests exercise the ordinary host path with a local broker and HTTP server. Run from
the repository root:

```powershell
./eng/verify-mapping-consumer.ps1
dotnet test tests/Unit/FluxFlow.Configuration.Tests -c Release --filter FullyQualifiedName~MapperConfigurationTests
dotnet test tests/Unit/FluxFlow.Components.Mqtt.Tests -c Release --filter FullyQualifiedName~Telemetry_configuration
```

These existing checks target .NET 8 and 10. For a real broker/API, use the
[telemetry sample instructions](../Code/samples/Hosting/FluxFlow.Hosting.Telemetry.Sample/README.md).

## Boundaries

Keep component behavior separate from setup. All addressable blocks carry FlowMessage;
FlowMessage<T> retains CLR values constrained to FlowValue. A component interprets its
own data and owns its blocks. DI owns the component. Registration assigns unique addresses
and selects blocks; it does not construct nodes or add metadata/activation wrappers.

Use the current checkout and coordinated package versions. Older migration plans are
historical evidence, not an alternative implementation guide. Optional expression
engines live in their own projects; deployment exporters, persistence and external
transport modules should not be inferred from the older documents.
