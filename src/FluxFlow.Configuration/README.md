# FluxFlow.Configuration

Ordinary setup traverses `IConfiguration` directly. It does not construct an
ApplicationDefinition/WorkflowDefinition/ComponentDefinition tree or serialize the
configuration root back to JSON. Each registrar receives its original section and
snapshots the settings needed by its deferred factory. JSON and in-memory providers
use this same path; other standard configuration providers can supply it too.
The former document models, reader, serializer and diagnostic compiler are removed.

`CompositionConfigurationException` belongs to this package and namespace. Native
resource registration and lookup helpers belong to `FluxFlow.Core.Application`;
they can be used without referencing this configuration reader package.

Component sections register their factories, native port selectors and LinkFactory
delegates together. Link declarations are parsed during AddFluxFlowApplicationAsync;
Build resolves the captured addresses and compiles conditions without rescanning the
application document. Parsing never constructs components or resolves an engine.
Malformed links and conflicting duplicate policies reject the staged registration.
Duplicate detection reads keyed LinkFactory registrations in the staged service
collection. Canonical addresses, condition text, parameter signature and engine
identify a route; conflicting delivery/fallback settings fail registration. Each
unique route has one keyed delegate and one unkeyed alias for application execution.
No parallel link dictionary is maintained. Parameter names are sorted at the top
level for identity; nested JSON property order retains its existing significance.
Missing target ports and invalid expressions still fail during Build, before Start.


## Typed configuration values

Port properties use `OneOrManyLinks` from `FluxFlow.Configuration.Links`:

```csharp
public sealed class MyConfiguration
{
    public OneOrManyLinks Input { get; init; } = OneOrManyLinks.Empty;
    public OneOrManyLinks Output { get; init; } = OneOrManyLinks.Empty;
    public ExpressionSource? Expression { get; init; }
    public int OutputCapacity { get; init; } = 128;
}

var config = section.ReadConfiguration<MyConfiguration>();
```

`ExpressionSource` belongs to `FluxFlow.Configuration.Expressions`. The port value
accepts an address string, one link object, or an array containing either form:

```json
"Output": [
  "audit.Input",
  { "Port": "send.Input", "Condition": "value.ready", "Engine": "csharp" }
]
```

Each item is a `LinkConfiguration` with Port, Condition, Engine, Parameters, Delivery
and Fallback. The same type works for input, output, control and event properties;
the registered native port determines direction. Missing properties retain Empty.
Malformed links fail with the configuration property path. Binding does not create
or register links; application setup still registers them from declared native ports,
including Router's dynamically named outputs. Both use the same link reader.

Use `ReadConfiguration<T>()` for DTOs with top-level `OneOrManyLinks` properties.
Standard `Get<T>()` does not understand the single-object/array union. The small
reader supports public setters/init properties and ConfigurationKeyName, preserves
defaults, and delegates other properties (including nested settings) to the standard
binder with conversion errors enabled. It is not a general recursive union binder;
nested OneOrManyLinks properties and immutable constructor-bound DTOs are outside
its contract. Existing resource DTOs can continue using native Get<T>().

ExpressionSource itself binds from a scalar with the native binder. Mapper.Expression
and Router.Routes values now use this type. For C# assignment, strings convert implicitly:

```csharp
ExpressionSource inline = "value.number * 2";
ExpressionSource file = "file:maps/request.jsonata";
ExpressionSource encoded = "base64:dmFsdWU=";
var text = file.Read(hostEnvironment.ContentRootPath);
```

## Batch

Register `services.AddBatch()` from `FluxFlow.Configuration.Batching` before
`AddFluxFlowApplicationAsync`. A component section can contain:

```json
"collect": {
  "Type": "batch",
  "BatchSize": 100,
  "MaxWait": "00:00:05",
  "Output": "consumer.Input"
}
```

BatchSize and MaxWait are required positive values. MaxWait starts with the first
collected message; reaching either size or time publishes the collected messages.
Normal completion flushes the remainder. OutputCapacity defaults to 128 and must
be positive. MaxWait must fit the native timer range (at most 4,294,967,294 ms).
Input, Output and named Events use the existing flat link syntax. The consumer must
be registered separately. Output values are FlowBatch snapshots containing the
original messages; incoming errors pass through without entering the batch.

Registration validates and snapshots settings without constructing Batch. Build
resolves the host TimeProvider, or uses TimeProvider.System, and constructs the
component once. Repeated Build preserves pending items and their deadline. Processing
remains ordered and serial; this adapter does not expose execution overrides.

## Router

Register `services.AddRouter(expressionBaseDirectory)` from
`FluxFlow.Configuration.Routing`, plus the chosen expression engine package.
The optional base directory defaults to AppContext.BaseDirectory, as for Mapper.

```json
{
  "workflows": {
    "main": {
      "route": {
        "type": "router",
        "engine": "csharp",
        "routes": {
          "Priority": "value.priority > 5 && headers.site == \"SE01\"",
          "Audit": "messageId != null"
        },
        "Priority": "urgent.Input",
        "Audit": "audit.Input",
        "Unmatched": "other.Input",
        "output": "errors.Input"
      }
    }
  }
}
```

The consumers in this fragment must be registered separately. Routes map output
names to boolean expressions; ordinary flat port properties specify links. Links
may also be declared at the destination input. Names are case-sensitive, follow the
normal port address rules, and cannot collide with Router configuration properties
or Input/Output/Unmatched/Events. At least one route is required. OutputCapacity defaults to 128.
All routes in one Router use its selected engine; `jsonata` is equally supported.

Reading configuration snapshots strings and registers factories/native port selectors.
Build reads inline/file expressions and compiles each once, before constructing Router.
No configuration or expression engine enters component logic. Predicates use the same
original message context as Mapper and conditional links: value, headers, string IDs
and timestamp. Every matching route receives the original message. Unmatched receives
normal messages when no condition matches. Output retains the existing error path;
predicate failure produces no partial route dispatch and never goes to Unmatched.
Configurations previously using Output for unmatched messages must move that link to
Unmatched and wire Output to their error consumer. No automatic discard sink is added.
Inline text, `.csx`/`.cs`/`.jsonata` files and explicit `inline:`/`file:`/`base64:` prefixes follow
Mapper's rules below. Repeated Build does not reread or recompile.

## Mapper and optional expression engines

```csharp
services.AddMapper(expressionBaseDirectory: hostEnvironment.ContentRootPath)
    .AddCSharpExpressionEngine()   // optional FluxFlow.Expressions.CSharp package
    .AddJsonataExpressionEngine(); // optional FluxFlow.Expressions.Jsonata package
await services.AddFluxFlowApplicationAsync(configuration);
await using var provider = services.BuildServiceProvider();
await using var app = await provider.BuildApplicationAsync();
await app.StartAsync();
```

Build the provider after registration, as in the complete example below. Engine
extension namespaces are FluxFlow.Expressions.CSharp and FluxFlow.Expressions.Jsonata;
AddMapper is in FluxFlow.Configuration.Mapping. Configuration references Mapping but
neither expression package; choose whichever engine packages your host needs.

```json
{
  "workflows": {
    "main": {
      "map": {
        "type": "mapper",
        "engine": "jsonata",
        "expression": "{'body': payload, 'id': $messageId}",
        "output": "send.Input"
      }
    }
  }
}
```

This fragment assumes `send` is registered separately. C# example: engine `csharp`,
expression `value.items[0].quantity * 2`. Engines are resolved by normalized lowercase
keys through standard keyed IFlowExpressionEngine registration. There is no type switch or
custom catalog. Additional engines can implement that same contract under another key.

MapperConfiguration is data-only. Registration snapshots settings and registers a
deferred component factory. Build resolves the compiler, reads the expression source,
compiles once and constructs Mapper. Invalid engine/file/syntax fails before Start,
and failed Build stops constructed components; provider teardown disposes them. Runtime data errors use
Mapper's existing FlowError path and preserve lineage. OutputCapacity defaults to 128;
other execution options retain component defaults in this configuration slice.

Expression accepts inline text or a path ending in `.jsonata`, `.csx` or `.cs`.
`file:` forces a path (any extension), `inline:` forces text, and `base64:` decodes UTF-8
text. Bare strings are never guessed to be Base64; invalid encoding fails. Decoded
text and file contents are not recursively interpreted as another source. Relative paths use the
single AddMapper base directory, default AppContext.BaseDirectory. Files are read at
Build, not per message; repeated Build on the same provider reuses the application. Missing/empty files fail,
with no fallback to interpreting the filename. The host should keep
expression files with the deployed configuration. Automatic hot reload is not included.

Both engines expose separate value/headers and string IDs (messageId, traceId,
correlationId, causationId); optional IDs can be null. C# timestamp is DateTimeOffset;
JSONata `$timestamp` is ISO 8601 text. JSONata also keeps the payload as `$` context.
Engine-specific README files describe numeric rules, expression-subset limitations,
and cooperative cancellation boundaries. Conditional links resolve the same
IFlowExpressionEngine registrations. Set Engine beside Condition on each link:
`{"Port":"sink.Input","Condition":"value > 0","Engine":"csharp"}`.
Application/workflow ExpressionEngine settings are removed. An omitted link Engine
uses the host's unkeyed default engine; an unknown explicit key fails Build, with no fallback.
Bundled engines expose that registration through a named option:

```csharp
services.AddJsonataExpressionEngine(useAsDefault: true);
services.AddCSharpExpressionEngine();
```

This keeps JSONata as the link default while both engines remain available by name.
Without `useAsDefault: true`, registration does not choose or replace a default.
If several registrations explicitly choose a default, the last one wins under
standard DI rules. Mapper and Router still require explicit Engine settings.

Conditions compile bool rather than FlowValue. Engine names are strings so hosts can
register custom engines without changing a framework enum.
Link conditions remain inline text; the file rules above apply to Mapper expressions.

Configuration registration and runtime construction have separate contracts:

```csharp
delegate void ComponentRegistration(IServiceCollection services, IConfigurationSection section);
delegate IFlowNode ComponentFactory(IServiceProvider services);
```

There is no activation context or component instance wrapper in the construction path.

The first reads settings and registers factories and port selectors. The second
constructs a fresh, unstarted component and returns it. Neither receives Application
or Workflow. Components themselves receive only options and explicit dependencies.
Component factories are synchronous. Reading configuration and building the service
provider do not invoke them; application composition invokes them when constructing nodes.
Network connection and background activity belong in asynchronous startup, not factories.
BuildApplicationAsync still awaits cleanup if construction fails; its name does not imply
that component factories are asynchronous.

The configuration root represents the application:

```json
{
  "workflows": {
    "demo": {
      "clock": { "type": "scheduler", "interval": "00:00:05", "output": "sink.Input" },
      "sink": { "type": "my_sink" }
    }
  }
}
```

```csharp
var services = new ServiceCollection().AddScheduler();
services.AddKeyedSingleton<ComponentRegistration>("my_sink", (registrations, section) =>
{
    var address = section.GetComponentAddress(); // demo.sink
    registrations.AddComponentFactory(address, provider => new MySink());
    registrations.AddInputPort<MySink>(address, "Input", node => node.Input);
});
await services.AddFluxFlowApplicationAsync(configuration);
await using var provider = services.BuildServiceProvider();
await using var app = await provider.BuildApplicationAsync();
await app.StartAsync();
// Keep the application alive until host shutdown, then dispose it to drain work.
```

Imports: FluxFlow.Configuration.Scheduling, FluxFlow.Configuration,
FluxFlow.Core.Application, and Microsoft.Extensions.DependencyInjection.
The host owns configuration providers and the service provider.

ComponentRegistration is registered as a keyed delegate instance by component type;
the last registration for a type is selected. It receives the standard service
collection and the exact component section. Registration failures do not partially
update the caller's service collection. Scheduler binds and validates settings here,
then captures them in its factory. Runtime dependencies are resolved by the factory.

AddComponentFactory registers a native keyed IFlowNode singleton under its unique
workflow.component address; its synchronous delegate is invoked lazily by DI.
Native DI does not return service keys when enumerating values, so workflow/component
names are also stored as ordinary read-only dictionary data in DI. There is no custom
catalog, registry class or activation context. Configuration registration does not
build a provider. AddFlowWorkflow
can preserve an empty workflow. AddInputPort, AddOutputPort and AddEventPort register
native keyed blocks, independently of construction. Each selector resolves the canonical
component singleton and runs once. Named events resolve the address-stamped bridge output.

BuildApplicationAsync composes the provider's single FlowApplication: it creates workflows,
resolves the keyed component and block singletons, invokes LinkFactory delegates and
validates links. It returns an unstarted application. Repeated Build calls return the same
Application and do not rerun factories or link delegates. LinkFactory receives the original
host provider; there is no temporary port provider or separate ordinary resource provider.
StartAsync connects prepared routes using those existing keyed blocks and starts sources;
it constructs no components. Build checks cancellation between synchronous factories
and stops partial construction on failure. DI owns disposal, including nodes constructed
before a Build failure. Constructors must not perform long-running I/O; source startup is
separate. One Application runs per host provider. Singleton components require dependencies
whose lifetimes are compatible with that provider; do not capture scoped dependencies.

For example, a link registration connects native blocks by address:

```csharp
services.AddSingleton<LinkFactory>(ports =>
{
    ports.Link("demo.clock.Output", "demo.sink.Input");
});
```

Link and LinkFallback accept full addresses; Link optionally accepts a native C#
predicate. Messages with no matching ordinary or fallback route retain the existing routing error behavior.
These extensions preserve fanout, delivery policy and completion coordination. Native
ISourceBlock/ITargetBlock instances are available by the same keys. Host dependencies are
available from that same provider; registrations are never copied into a second provider.
Components receive explicit dependencies, not the provider used for composition.

Stop/dispose Application before disposing the host provider. Application drains execution;
DI disposes its components and resources. Standard DI disposal stops on the first disposal
failure; FluxFlow adds no aggregate cleanup manager. A factory cannot return a node already
registered under another key/provider. A constructor that throws before returning must
clean up resources it acquired outside DI.

Declared port sections are read directly, preserving arrays,
conditions, fallback, delivery policies and custom port names. Each parsed link
registers its own LinkFactory. No temporary JSON link document or application-wide
JSON scan occurs. Bound expression parameters retain their existing JSON conversion
and are snapshotted during registration.
Resource sections invoke a type-keyed
`FluxFlow.Configuration.ResourceRegistration(IServiceCollection, ApplicationAddress,
IConfigurationSection)` during AddFluxFlowApplicationAsync. That callback registers
native resource factories through AddFluxFlowResource; it never receives a host provider.
Unknown resource types fail registration rather than silently going unhandled.
Each resource adapter validates its own settings during Add. Invalid settings report
the resource address without changing the host's registrations. Resource types are
registered explicitly by their adapters; no built-in processing profile is supplied.
Registration does not instantiate resources; they are native services in the same host
collection. DI constructs dependencies before their components and disposes in reverse
construction order. No automatic event/error aggregation is added.

Example (registration code, not component behavior):

```csharp
services.AddKeyedSingleton<ResourceRegistration>("connection", (resources, address, section) =>
{
    var config = section.Get<ConnectionConfiguration>()
        ?? throw new InvalidOperationException("Missing connection configuration.");
    ArgumentException.ThrowIfNullOrWhiteSpace(config.Endpoint);
    var endpoint = config.Endpoint;
    resources.AddFluxFlowResource(address, _ => new Connection(endpoint));
});
```

ConnectionConfiguration is a resource-specific, data-only class with an Endpoint property.
Each resource adapter owns its configuration class, just like a component adapter.
Resource registrars bind their own DTO directly from the section; no shared resource
definition model or separate document API is involved.
Use the Configuration delegate taking IConfigurationSection. Read and validate settings
inside the registration callback; do not defer reading the mutable section until Build.
Registration happens now, not inside Build. Code-only callers can use
`services.AddApplicationResources(resources => { /* native DI registrations */ });`.
The callback executes immediately; resource descriptors are frozen and repeated calls
merge registrations transactionally. Build resolves those services without replaying
registration callbacks or creating a resource provider. DI owns resource singletons.

FlowApplication coordinates component resolution and MakeLinks internally;
BuildApplicationAsync resolves the Application singleton and invokes composition once.
StartAsync remains explicit. Application retains the borrowed provider for composition
lookup; business components do not receive it. Workflow port dictionaries and manual
construction APIs are removed. Ports resolve only from native keyed DI. Port selectors
must return non-disposable blocks, keeping cleanup with the component and preventing
DI from capturing component-owned aliases. Provider disposal follows standard .NET
behavior, including stopping at the first disposer that throws.

Use dependencies.GetRequiredFlowResource<T>(reference, workflowName) inside a factory
for relative, full or explicit host-key resource lookup. A named AddEventPort overload
also exposes routable events with source/workflow/component headers; selecting and
naming these ports remains composition's responsibility.

Configuration can be the root or a selected subsection. Input-side declarations
such as `"input": "clock.Output"` and cross-workflow addresses remain supported.
Settings are captured at registration. Later configuration changes do not update the
application; hot reload is not implemented on this path.

For Scheduler specify exactly one of positive interval, Cronos cron, or at
(ISO-8601 DateTimeOffset). Cron alone accepts timeZone (UTC by default) and
includeSeconds (false by default). OutputCapacity defaults to 128 and must be
positive. Invalid settings fail registration with the component address. A past
at is valid and emits nothing. The runtime factory uses the host TimeProvider
when registered, otherwise TimeProvider.System.

See eng/configuration-consumer/Program.cs for a complete executable example.

Gate and Source use the same registration path. Add `AddGate()` from
`FluxFlow.Configuration.Routing` and `AddSource()` from
`FluxFlow.Configuration.Sources` alongside `AddScheduler()`.

```json
{
  "workflows": {
    "demo": {
      "clock": { "type": "scheduler", "interval": "00:00:05", "output": "samples.Input" },
      "samples": { "type": "source", "valuesJson": "[123,\"123\",true,null,{\"code\":\"001\"}]", "output": "gate.Input" },
      "gate": { "type": "gate", "initiallyOpen": true, "output": "sink.Input" },
      "sink": { "type": "my_sink" }
    }
  }
}
```

Gate defaults to closed. It registers Input, Open, Close and Output; Open/Close are
native control inputs. Closed gates discard normal and error messages. InitiallyOpen
and positive OutputCapacity (default 128) are configuration settings; other native
execution options retain their component defaults in this initial registration.

Source emits one sample for each ordinary Input, in order. It does not schedule
itself, repeat the list or emit after exhaustion. ValuesJson is required JSON **text**
containing an array; an empty array is valid. OutputCapacity defaults to 128 and must
be positive. Parsing occurs once at registration; immutable values survive disposal
of the temporary JsonDocument. Each provider owns one Source instance and its cursor;
repeated Build on that provider reuses it without restarting the sample sequence.

Using JSON text here is deliberate: IConfiguration exposes string values and cannot
preserve the original distinction between `123` and `"123"` in a heterogeneous array.
ValuesJson preserves that distinction, nested objects/arrays and null with standard
System.Text.Json. Quotes inside the string need JSON escaping as shown. No file loader,
general-purpose converter, inferred payload type or new serialization layer is added.

Configuration references the Core, Batch, Mapping, Scheduler, Routing and Source packages.
Component projects remain independent of Configuration. DTOs contain data only;
registration methods map them to constructor arguments/options and native port selectors.
