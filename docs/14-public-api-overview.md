# Public API overview

This page describes the current minimum-framework checkout. The package manifest is
[eng/packages.json](../Code/eng/packages.json); all packages currently built from it are:

- `FluxFlow.Expressions.CSharp` — [src/Expressions/FluxFlow.Expressions.CSharp/FluxFlow.Expressions.CSharp.csproj](../Code/src/Expressions/FluxFlow.Expressions.CSharp/FluxFlow.Expressions.CSharp.csproj)
- `FluxFlow.Expressions.Jsonata` — [src/Expressions/FluxFlow.Expressions.Jsonata/FluxFlow.Expressions.Jsonata.csproj](../Code/src/Expressions/FluxFlow.Expressions.Jsonata/FluxFlow.Expressions.Jsonata.csproj)
- `FluxFlow.Configuration` — [src/FluxFlow.Configuration/FluxFlow.Configuration.csproj](../Code/src/FluxFlow.Configuration/FluxFlow.Configuration.csproj)
- `FluxFlow.Application` — [src/FluxFlow.Application/FluxFlow.Application.csproj](../Code/src/FluxFlow.Application/FluxFlow.Application.csproj)
- `FluxFlow.Core` — [src/FluxFlow.Core/FluxFlow.Core.csproj](../Code/src/FluxFlow.Core/FluxFlow.Core.csproj)
- `FluxFlow.Components.Routing` — [src/Plugins/FluxFlow.Plugins.Routing/Runtime/FluxFlow.Components.Routing/FluxFlow.Components.Routing.csproj](../Code/src/Plugins/FluxFlow.Plugins.Routing/Runtime/FluxFlow.Components.Routing/FluxFlow.Components.Routing.csproj)
- `FluxFlow.Components.Mapping` — [src/Plugins/FluxFlow.Plugins.Transformation/Runtime/FluxFlow.Components.Mapping/FluxFlow.Components.Mapping.csproj](../Code/src/Plugins/FluxFlow.Plugins.Transformation/Runtime/FluxFlow.Components.Mapping/FluxFlow.Components.Mapping.csproj)
- `FluxFlow.Components.Resilience` — [src/Plugins/FluxFlow.Plugins.FlowControl/Runtime/FluxFlow.Components.Resilience/FluxFlow.Components.Resilience.csproj](../Code/src/Plugins/FluxFlow.Plugins.FlowControl/Runtime/FluxFlow.Components.Resilience/FluxFlow.Components.Resilience.csproj)
- `FluxFlow.Components.Source` — [src/Plugins/FluxFlow.Plugins.Sources/Runtime/FluxFlow.Components.Source/FluxFlow.Components.Source.csproj](../Code/src/Plugins/FluxFlow.Plugins.Sources/Runtime/FluxFlow.Components.Source/FluxFlow.Components.Source.csproj)
- `FluxFlow.Components.Scheduler` — [src/Plugins/FluxFlow.Plugins.Sources/Runtime/FluxFlow.Components.Scheduler/FluxFlow.Components.Scheduler.csproj](../Code/src/Plugins/FluxFlow.Plugins.Sources/Runtime/FluxFlow.Components.Scheduler/FluxFlow.Components.Scheduler.csproj)
- `FluxFlow.Components.Reducer` — [src/Plugins/FluxFlow.Plugins.Transformation/Runtime/FluxFlow.Components.Reducer/FluxFlow.Components.Reducer.csproj](../Code/src/Plugins/FluxFlow.Plugins.Transformation/Runtime/FluxFlow.Components.Reducer/FluxFlow.Components.Reducer.csproj)
- `FluxFlow.Components.RateLimit` — [src/Plugins/FluxFlow.Plugins.FlowControl/Runtime/FluxFlow.Components.RateLimit/FluxFlow.Components.RateLimit.csproj](../Code/src/Plugins/FluxFlow.Plugins.FlowControl/Runtime/FluxFlow.Components.RateLimit/FluxFlow.Components.RateLimit.csproj)
- `FluxFlow.Components.Batch` — [src/Plugins/FluxFlow.Plugins.Transformation/Runtime/FluxFlow.Components.Batch/FluxFlow.Components.Batch.csproj](../Code/src/Plugins/FluxFlow.Plugins.Transformation/Runtime/FluxFlow.Components.Batch/FluxFlow.Components.Batch.csproj)
- `FluxFlow.Components.ChangeFilter` — [src/Plugins/FluxFlow.Plugins.Routing/Runtime/FluxFlow.Components.ChangeFilter/FluxFlow.Components.ChangeFilter.csproj](../Code/src/Plugins/FluxFlow.Plugins.Routing/Runtime/FluxFlow.Components.ChangeFilter/FluxFlow.Components.ChangeFilter.csproj)
- `FluxFlow.Components.Deduplicate` — [src/Plugins/FluxFlow.Plugins.Routing/Runtime/FluxFlow.Components.Deduplicate/FluxFlow.Components.Deduplicate.csproj](../Code/src/Plugins/FluxFlow.Plugins.Routing/Runtime/FluxFlow.Components.Deduplicate/FluxFlow.Components.Deduplicate.csproj)
- `FluxFlow.Components.Ros2` — [src/Plugins/FluxFlow.Plugins.Ros2/Runtime/FluxFlow.Components.Ros2/FluxFlow.Components.Ros2.csproj](../Code/src/Plugins/FluxFlow.Plugins.Ros2/Runtime/FluxFlow.Components.Ros2/FluxFlow.Components.Ros2.csproj)
- `FluxFlow.Components.DataGeneration` — [src/Plugins/FluxFlow.Plugins.Sources/Runtime/FluxFlow.Components.DataGeneration/FluxFlow.Components.DataGeneration.csproj](../Code/src/Plugins/FluxFlow.Plugins.Sources/Runtime/FluxFlow.Components.DataGeneration/FluxFlow.Components.DataGeneration.csproj)
- `FluxFlow.Components.Mqtt` — [connection, publisher and subscriber setup](../Code/src/Plugins/FluxFlow.Plugins.Mqtt/Runtime/Configuration/README.md)
- `FluxFlow.Components.Http` — [outbound HTTP client setup](../Code/src/Plugins/FluxFlow.Plugins.Http/README.md)
- `FluxFlow.Components.Serial` — [workflow-local serial setup](../Code/src/Plugins/FluxFlow.Plugins.Serial/Runtime/Configuration/README.md)
- `FluxFlow.Plugins.Serial.Settings` — [serial settings and limits](../Code/src/Plugins/FluxFlow.Plugins.Serial/Settings/README.md)

## Ordinary construction and execution

The broker families also publish their runtime dependencies separately:

- `FluxFlow.Plugins.Mqtt.Settings` contains portable MQTT settings and validation.
- `FluxFlow.Components.Nats` contains NATS and JetStream runtime components.
- `FluxFlow.Plugins.Nats.Settings` contains portable NATS and JetStream settings and validation.
- `FluxFlow.Components.RabbitMQ` contains RabbitMQ runtime components.
- `FluxFlow.Plugins.RabbitMQ.Settings` contains portable AMQP settings and declaration validation.
- `FluxFlow.Plugins.Http.Settings` contains portable HTTP client and authentication settings.

The complete Studio plugins bundle these dependencies with their portable editors.

`FluxFlow.Hosting.AspNetCore` maps configured `http.trigger` components with
`AddHttpTriggers` and `MapFluxFlowHttpTriggers`. The native route group supports host
policies and filters. `FluxFlow.Components.Http` supplies the server-independent
`HttpTrigger` and `HttpReply`. See the
[ASP.NET Core hosting guide](../Code/src/Plugins/FluxFlow.Plugins.Http/Hosting/FluxFlow.Hosting.AspNetCore/README.md).

`FluxFlow.Components.Logging` supplies Logger, LoggerOptions and LogRequest. Configuration
registers AddLogger, LoggerConfiguration and OtlpLoggerConfiguration; see the
[logging setup guide](../Code/src/Plugins/FluxFlow.Plugins.Logging/Registration/README.md).

`FluxFlow.Components.DataGeneration` supplies DataGenerator and DataGeneratorOptions.
Setup is in `FluxFlow.Configuration.Components.DataGeneration` with AddDataGenerator
and DataGeneratorConfiguration. See the
[setup guide](../Code/src/Plugins/FluxFlow.Plugins.Sources/Registration/DataGenerator.md).

Application composition now ships in FluxFlow.Application, including Addressing and
Execution subnamespaces. Replace imports of FluxFlow.Core.Application,
FluxFlow.Core.Addressing and FluxFlow.Core.Execution and reference the new package.
Core no longer references DI or Hosting. No compatibility forwarding layer is provided.
Configuration depends on Application. Component families can also depend on their
portable settings packages; neither runtime nor settings packages depend on Studio.


Resource DI helpers are in `FluxFlow.Application`, alongside component
registration. `ResourceRegistrationExtensions` replaces the old
`FluxFlowServiceCollectionExtensions` class. `CompositionConfigurationException`
is in the `FluxFlow.Configuration.Application` namespace, in the Configuration assembly. The Core.Configuration
namespace is removed; callers must update imports and rebuild.

Configuration folders and namespaces now align: application registration and resources
are under FluxFlow.Configuration.Application, with links under Application.Links.
Configuration retains the shared binding and registrar contracts. Family-specific
DTOs and registration adapters now live with their owning plugins, in runtime,
settings or registration projects. Code consumers must reference the owning package
and rebuild after these assembly moves. Saved workflow type names remain unchanged.
ExpressionSource and JsonSource retain their shared Expressions and Sources namespaces.

Register configuration with AddFluxFlowApplication before building the host provider.
This synchronous IServiceCollection extension replaces AddFluxFlowApplicationAsync;
remove the await and registration cancellation argument. Runtime build and lifetime
methods remain asynchronous. ReadRequiredConfiguration<T> provides the required-section
variant of ReadConfiguration<T>, with the same binding and scoped link behavior.
ComponentFactory constructs one component synchronously with explicit dependencies.
AddInputPort, AddOutputPort and AddEventPort assign addresses to native blocks selected
from that instance. ApplicationAddress.Value is the DI key. No metadata record or catalog
is needed for component registration.

BuildApplicationAsync resolves the registered FlowApplication, prepares its components
and links once, and returns it unstarted. StartAsync starts processing; StopAsync drains.
DI owns disposal of the components and resources. FlowWorkflow.StartAsync/StopAsync
control an independent workflow run; restart creates fresh scoped components and ports.
Resolve them through workflow.Services or provider.GetWorkflowServices(name).
Ordinary generic hosting uses AddFluxFlow().

Host discovery uses GetComponentAddresses, GetInputPortAddresses and
GetOutputPortAddresses on the existing IServiceProvider without constructing components.
ObserveOutput returns a disposable FlowOutputProbe with Address, Messages and DroppedCount.
It observes output offers, including Events, with bounded sampling and no business-message
consumption. Input/link observation and actual Windows Service verification remain pending.
See [hosting and observability](05-hosting-and-observability.md) for the current scope.

FlowApplicationExtensions replaces FlowExtensions and also owns AddFlowWorkflow and
BuildApplicationAsync, moved from ComponentRegistrationExtensions. AddApplicationResources
now belongs to ResourceRegistrationExtensions. Extension calls and namespace imports
are unchanged; explicit static calls and using-static directives must use the new
declaring classes. Rebuild dependent binaries; no compatibility aliases are provided.

FlowApplication.Workflows is now IReadOnlyCollection<FlowWorkflow>, not a dictionary.
Enumerate it by workflow.Name; resolve a named workflow through
provider.GetRequiredKeyedService<FlowWorkflow>(name). The workflow/component lookup
dictionaries are removed. These workflow views require a live host provider.

## Messages and component code

FluxFlow.Core.Messaging contains FlowMessage, FlowMessage<T> where T : FlowValue,
FlowValue and FlowError. WithValue preserves lineage when creating a new payload.
FluxFlow.Core.Nodes contains the component wrappers and native processing lifecycle.
Components expose blocks and accept local options/dependencies; configuration, naming,
registration and expression-engine selection remain outside component behavior.

Mapping contracts are in FluxFlow.Core.Mapping. Optional expression packages depend
on Core; components receive ordinary delegates. See the configuration package
README for available adapters and expression parameters.

## Configuration setup

The data-only ComponentConfiguration record supplies standard Input/Output/Events/Error
properties. OutputComponentConfiguration omits Input for source-only components.
Registrars call services.AddLinks(componentAddress, config) after declaring native ports;
special ports still use the directional helpers explicitly. MapperConfiguration and
RouterConfiguration are now sealed records, like the other bundled configuration DTOs.
Custom DTOs can keep their own shape and call the existing individual link helpers.

Setup traverses IConfiguration directly; no intermediate document models, serializers or document diagnostic compiler remain. Register components with AddComponentFactory and native ports; LinkFactory applies parsed links or native C# predicates. FluxFlow.Configuration.Application.ResourceRegistration receives IServiceCollection, ApplicationAddress and IConfigurationSection and registers resource factories during configuration loading.

Configuration link APIs accept PortAddress, not strings or component/name pairs.
GetComponentAddress returns ComponentAddress; its Port(name) method constructs a
validated port. PortReference.Address is also PortAddress, resolved during DTO
binding. Both immutable address values live in Core.Addressing and reuse ApplicationAddress
validation/equality rules. Use Value only when an existing native DI/runtime API needs
the canonical string key. Native port existence/direction remain Build-time checks.

Intentional preview API changes update the public API baseline in eng/public-api.

The current baseline includes the plugin settings split, the optional JSONata
function registration parameter, RequestReplyResult outcomes, and Serial Reader
and Writer contracts. Serial settings and HTTP trigger configuration keep their
existing runtime type forwards. This source baseline does not certify binary
compatibility with earlier preview packages; existing compiled consumers must be
rebuilt when method signatures or registration assemblies change.
## ROS 2 components and setup

- [FluxFlow.Components.Ros2](../Code/src/Plugins/FluxFlow.Plugins.Ros2/Runtime/FluxFlow.Components.Ros2/README.md):
  RosPublisher<T>, RosSubscriber<T> and their options.
- [FluxFlow.Configuration.Components.Ros2](../Code/src/Plugins/FluxFlow.Plugins.Ros2/README.md):
  AddRosNode, AddRosPublisher<T>, AddRosSubscriber<T>
  and data-only configuration classes in the existing Configuration package.
