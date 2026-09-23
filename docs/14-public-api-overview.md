# Public API overview

This page describes the current minimum-framework checkout. The package manifest is
[eng/packages.json](../eng/packages.json); all packages currently built from it are:

- `FluxFlow.Expressions.CSharp` — [src/Expressions/FluxFlow.Expressions.CSharp/FluxFlow.Expressions.CSharp.csproj](../src/Expressions/FluxFlow.Expressions.CSharp/FluxFlow.Expressions.CSharp.csproj)
- `FluxFlow.Expressions.Jsonata` — [src/Expressions/FluxFlow.Expressions.Jsonata/FluxFlow.Expressions.Jsonata.csproj](../src/Expressions/FluxFlow.Expressions.Jsonata/FluxFlow.Expressions.Jsonata.csproj)
- `FluxFlow.Configuration` — [src/FluxFlow.Configuration/FluxFlow.Configuration.csproj](../src/FluxFlow.Configuration/FluxFlow.Configuration.csproj)
- `FluxFlow.Core` — [src/FluxFlow.Core/FluxFlow.Core.csproj](../src/FluxFlow.Core/FluxFlow.Core.csproj)
- `FluxFlow.Components.Routing` — [src/Components/FluxFlow.Components.Routing/FluxFlow.Components.Routing.csproj](../src/Components/FluxFlow.Components.Routing/FluxFlow.Components.Routing.csproj)
- `FluxFlow.Components.Mapping` — [src/Components/FluxFlow.Components.Mapping/FluxFlow.Components.Mapping.csproj](../src/Components/FluxFlow.Components.Mapping/FluxFlow.Components.Mapping.csproj)
- `FluxFlow.Components.Resilience` — [src/Components/FluxFlow.Components.Resilience/FluxFlow.Components.Resilience.csproj](../src/Components/FluxFlow.Components.Resilience/FluxFlow.Components.Resilience.csproj)
- `FluxFlow.Components.Source` — [src/Components/FluxFlow.Components.Source/FluxFlow.Components.Source.csproj](../src/Components/FluxFlow.Components.Source/FluxFlow.Components.Source.csproj)
- `FluxFlow.Components.Scheduler` — [src/Components/FluxFlow.Components.Scheduler/FluxFlow.Components.Scheduler.csproj](../src/Components/FluxFlow.Components.Scheduler/FluxFlow.Components.Scheduler.csproj)
- `FluxFlow.Components.Reducer` — [src/Components/FluxFlow.Components.Reducer/FluxFlow.Components.Reducer.csproj](../src/Components/FluxFlow.Components.Reducer/FluxFlow.Components.Reducer.csproj)
- `FluxFlow.Components.RateLimit` — [src/Components/FluxFlow.Components.RateLimit/FluxFlow.Components.RateLimit.csproj](../src/Components/FluxFlow.Components.RateLimit/FluxFlow.Components.RateLimit.csproj)
- `FluxFlow.Components.Batch` — [src/Components/FluxFlow.Components.Batch/FluxFlow.Components.Batch.csproj](../src/Components/FluxFlow.Components.Batch/FluxFlow.Components.Batch.csproj)
- `FluxFlow.Components.ChangeFilter` — [src/Components/FluxFlow.Components.ChangeFilter/FluxFlow.Components.ChangeFilter.csproj](../src/Components/FluxFlow.Components.ChangeFilter/FluxFlow.Components.ChangeFilter.csproj)
- `FluxFlow.Components.Deduplicate` — [src/Components/FluxFlow.Components.Deduplicate/FluxFlow.Components.Deduplicate.csproj](../src/Components/FluxFlow.Components.Deduplicate/FluxFlow.Components.Deduplicate.csproj)

## Ordinary construction and execution

Resource DI helpers are in `FluxFlow.Core.Application`, alongside component
registration. `ResourceRegistrationExtensions` replaces the old
`FluxFlowServiceCollectionExtensions` class. `CompositionConfigurationException`
is in the `FluxFlow.Configuration` namespace and assembly. The Core.Configuration
namespace is removed; callers must update imports and rebuild.

Register configuration with AddFluxFlowApplicationAsync before building the host provider.
ComponentFactory constructs one component synchronously with explicit dependencies.
AddInputPort, AddOutputPort and AddEventPort assign addresses to native blocks selected
from that instance. ApplicationAddress.Value is the DI key. No metadata record or catalog
is needed for component registration.

BuildApplicationAsync resolves the registered FlowApplication, prepares its components
and links once, and returns it unstarted. StartAsync starts processing; StopAsync drains.
DI owns disposal of the components and resources. Workflows have no Start/Stop methods.
Ordinary generic hosting uses AddFluxFlow().

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

Setup traverses IConfiguration directly; no intermediate document models, serializers or document diagnostic compiler remain. Register components with AddComponentFactory and native ports; LinkFactory applies parsed links or native C# predicates. FluxFlow.Configuration.ResourceRegistration receives IServiceCollection, ApplicationAddress and IConfigurationSection and registers resource factories during configuration loading.

Intentional preview API changes update the public API baseline in eng/public-api.
