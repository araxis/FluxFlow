# FluxFlow.Configuration

The [ROS 2 plugin](../Plugins/FluxFlow.Plugins.Ros2/README.md) supplies typed topic and node registrations.
For workflow-local serial ports, see the [Serial plugin](../Plugins/FluxFlow.Plugins.Serial/README.md).

Ordinary setup traverses `IConfiguration` directly. It does not construct an
ApplicationDefinition/WorkflowDefinition/ComponentDefinition tree or serialize the
configuration root back to JSON. Each registrar receives its original section and
snapshots the settings needed by its deferred factory. JSON and in-memory providers
use this same path; other standard configuration providers can supply it too.
The former document models, reader, serializer and diagnostic compiler are removed.

Registration is synchronous: call `services.AddFluxFlowApplication(configuration)`.
It returns the same service collection and does not build a provider. The former
async entry point and its cancellation argument are removed; runtime build/start/stop
remain asynchronous. Registration still stages descriptors and commits only after
all sections succeed.

`CompositionConfigurationException` belongs to `FluxFlow.Configuration.Application`
in this package. Native
resource registration and lookup helpers belong to `FluxFlow.Application`;
they can be used without referencing this configuration reader package.

## Project layout

Folders and namespaces follow the same responsibility boundaries:

```text
FluxFlow.Configuration/
  Application/          Application registration, resource registrar contract and errors
    Links/              Link values, registration helpers and condition compilation
  Components/           Shared component DTOs, binding and registrar contract
  Expressions/          ExpressionSource and internal compilation extensions
  Sources/              Shared JsonSource value
```

Each plugin owns its component settings and registration extensions. For example,
the [Sources plugin](../Plugins/FluxFlow.Plugins.Sources/README.md) supplies
`FluxFlow.Configuration.Components.Scheduling`. The shared configuration package
provides the binding helpers in `FluxFlow.Configuration.Components` and application
and link APIs in `FluxFlow.Configuration.Application` and its `Links` namespace.

Component sections register their factories, native port selectors and LinkFactory
delegates together. Link declarations are parsed during AddFluxFlowApplication;
Build resolves the captured addresses and compiles conditions without rescanning the
application document. Parsing never constructs components or resolves an engine.
Malformed links reject the staged registration.
Duplicate detection reads keyed LinkFactory registrations in the staged service
collection. Canonical addresses, condition text, engine and the error-only selection
identify a route; equal declarations are registered once. Each
unique route has one keyed delegate and one unkeyed alias for application execution.
No parallel link dictionary is maintained.
Each singular helper explicitly supplies source and target to the same registration
method. Plural helpers simply call the corresponding singular helper for each reference;
there is no internal direction flag. Error links retain their error-only filter and identity.
Missing target ports and invalid expressions still fail during Build, before Start.

Conditional links share one native keyed DI registration per exact engine/expression
pair. The registered Lazy value compiles only when Build connects the links and retains
either the compiled delegate or its failure for that provider. There is no separate
compiler service or lookup dictionary. Evaluation still runs for each message with its
current context; only compilation is reused. Default and explicitly named engines have
distinct keys, and unconditional links require no expression registration.

Mapper, Router and conditional links share small internal extensions for exact engine
lookup and compilation-result validation. Mapper/Router still require an explicit engine;
only links may use the unkeyed default. ExpressionSource retains file/text handling.
Router resolves one engine for all routes. Components use the normal Compile overload;
links retain their input/payload/message type hints and existing Lazy reuse. A custom
engine returning no compiled delegate fails during Build, before messages are processed.


## Typed configuration values

A DTO can declare one link or a collection explicitly:

```csharp
public sealed class ReplyConfiguration
{
    public PortReference? Reply { get; init; }
    public OneOrManyPortReferences Output { get; init; } = OneOrManyPortReferences.Empty;
}

var address = section.GetComponentAddress();
var config = section.ReadRequiredConfiguration<ReplyConfiguration>();
if (config.Reply is { } reply)
    services.AddInputLink(address.Port("Reply"), reply);
services.AddOutputLinks(address.Port("Output"), config.Output);
```

PortReference.Read(section, componentAddress) owns the single-link parser.
ReadConfiguration/ReadRequiredConfiguration use it for top-level PortReference
properties, and OneOrManyPortReferences reuses it for every item. A single link accepts an address
string or an Address/Condition/Engine object; an array is rejected with its section path.
Missing properties retain their DTO default. The registrar decides whether a missing
link is allowed. Arrays with multiple bad entries still report all item locations.

AddInputLink, AddOutputLink and AddErrorLink accept a bound PortReference and preserve
its condition and engine. They share registration and duplicate identity with the plural
helpers. The error helper still filters the existing Output; it creates no new port.
One link per configuration property does not restrict the number of other links to the
runtime port. Configuration arrays and existing bundled DTOs are unchanged.

For a single connection, use two validated port addresses directly:

```csharp
services.AddLink(sourceAddress.Port("Output"), targetAddress.Port("Input"));
```

AddLink takes source first and target second; it needs no OneOrManyPortReferences or configuration
DTO. It registers a deferred LinkFactory, with the same duplicate identity as an
unconditional link declared through AddInputLinks or AddOutputLinks. Those plural helpers
retain their directional convenience for bound collections. All use one registration
implementation. A component-specific limit of one configured connection is a separate
validation rule; AddLink itself neither prohibits fanout nor restricts other registrations.

Port properties use `OneOrManyPortReferences` from `FluxFlow.Configuration.Application.Links`:

```csharp
public sealed record MyConfiguration : ComponentConfiguration
{
    public ExpressionSource? Expression { get; init; }
    public int OutputCapacity { get; init; } = 128;
}

var config = section.ReadRequiredConfiguration<MyConfiguration>();
```

`ExpressionSource` belongs to `FluxFlow.Configuration.Expressions`. The port value
accepts an address string, one link object, or an array containing either form:

```json
"Output": [
  "audit.Input",
  { "Address": "send.Input", "Condition": "value.ready", "Engine": "csharp" }
]
```

Each item is a `PortReference` with a typed `PortAddress` Address, Condition and Engine.
The same type works for input, output, control and event properties;
the registrar explicitly chooses direction and the native port. Missing properties retain Empty.
Malformed links fail with the configuration property path. Binding does not create
or register links. Inside the component registrar, after registering its native ports:

```csharp
var address = section.GetComponentAddress(); // ComponentAddress
services.AddLinks(address, config);
```

The helpers register deferred LinkFactory delegates from these already parsed values.
ComponentConfiguration supplies Input, Output, Events and Error. OutputComponentConfiguration
supplies only the outgoing properties for components such as Scheduler. Both are plain
configuration records; they contain no conversion, registration or runtime behavior.
AddLinks is an explicit registrar call for those standard names. Gate's Open/Close,
Router's Unmatched/named routes, and custom port names still use AddInputLinks or
AddOutputLinks explicitly. Custom DTOs do not have to inherit either record.
ApplicationConfigurationExtensions does not discover port properties. Custom registrars must
explicitly register their links as above; a native port registration alone is insufficient.
Router reads its known dynamically named output properties inside its own registrar,
using OneOrManyPortReferences.Read(property, address) for each present route property.

ComponentAddress and PortAddress are immutable validated values in Core.Addressing.
They reuse ApplicationAddress rules; neither accepts an implicit string conversion.
ReadConfiguration resolves every link reference to a full PortAddress using the
component section's workflow (including a host-selected root). Invalid syntax fails
during this DTO read with its configuration property path. Link helpers accept only
PortAddress and do not parse or resolve strings. For standalone link sections, supply
the containing ComponentAddress explicitly to OneOrManyPortReferences.Read. Native DI/runtime
APIs that accept string keys receive address.Value. Existence and port direction are
still verified at Build; a syntactically valid address does not guarantee a registered port.

Migration: GetComponentAddress now returns ComponentAddress; PortReference.Address
now holds PortAddress and its Value is the full address, not the original relative text.
Replace AddInputLinks(address, "Input", links) with AddInputLinks(address.Port("Input"), links)
and similarly for outputs; Error takes the actual Output port address. DTOs with link
properties must be read from a workflow component section so relative references have
a real scope. The flat JSON syntax itself is unchanged.

Error uses the same link syntax and selects only IsError messages from Output before
evaluating any optional Condition. It needs no engine when Condition is omitted.
It creates no Error port or component option; an address such as node.Error is invalid
unless a custom component explicitly declares that real port. Output still includes
errors, so configuring both paths can deliver an error to both destinations, even twice
to the same destination. Identical Error declarations are deduplicated. The original
envelope, payload, headers and IDs are preserved. All bundled adapters support Error.

Use `ReadRequiredConfiguration<T>()` in registrars for DTOs with top-level
`OneOrManyPortReferences` or `PortReference` properties. It reports the DTO name and section path when missing.
`ReadConfiguration<T>()` remains available for optional sections and returns null
when absent. Both use the same reader.
DTO sections must contain child settings; a scalar section (including one with child
values from another provider) is rejected with its path instead of returning defaults.
Standard `Get<T>()` does not understand the single-object/array union. The small
reader supports public setters/init properties and ConfigurationKeyName, preserves
defaults, and delegates other properties (including nested settings) to the standard
binder with conversion errors enabled. It is not a general recursive union binder;
nested OneOrManyPortReferences properties and immutable constructor-bound DTOs are outside
its contract. Existing resource DTOs can continue using native Get<T>().

This reader currently uses reflection for component-owned DTO properties. The
configuration binding source generator is not enabled in this package; the current
resource DTOs also use required and init-only properties whose binding must be preserved.
Core's generated JSON metadata does not make configuration binding reflection-free.

ExpressionSource itself binds from a scalar with the native binder. Mapper.Expression
and Router.Routes values now use this type. For C# assignment, strings convert implicitly:

```csharp
ExpressionSource inline = "value.number * 2";
ExpressionSource file = "file:maps/request.jsonata";
ExpressionSource encoded = "base64:dmFsdWU=";
var text = file.Read(hostEnvironment.ContentRootPath);
```

## Batch

Register `services.AddBatch()` from `FluxFlow.Configuration.Components.Batching` before
`AddFluxFlowApplication`. A component section can contain:

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
`FluxFlow.Configuration.Components.Routing`, plus the chosen expression engine package.
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
Unmatched and wire Output to their error consumer. At application runtime, registered
outputs without matching links drain normally, including Unmatched and Output.
Inline text, `.csx`/`.cs`/`.jsonata` files and explicit `inline:`/`file:`/`base64:` prefixes follow
Mapper's rules below. Repeated Build does not reread or recompile.

## Mapper and optional expression engines

```csharp
services.AddMapper(expressionBaseDirectory: hostEnvironment.ContentRootPath)
    .AddCSharpExpressionEngine()   // optional FluxFlow.Expressions.CSharp package
    .AddJsonataExpressionEngine(); // optional FluxFlow.Expressions.Jsonata package
services.AddFluxFlowApplication(configuration);
await using var provider = services.BuildServiceProvider();
await using var app = await provider.BuildApplicationAsync();
await app.StartAsync();
```

Build the provider after registration, as in the complete example below. Engine
extension namespaces are FluxFlow.Expressions.CSharp and FluxFlow.Expressions.Jsonata;
AddMapper is in FluxFlow.Configuration.Components.Mapping. Configuration references Mapping but
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
expression `value.items[0].quantity * 2`. Engine values match standard keyed
IFlowExpressionEngine registrations exactly, including casing and whitespace, in Mapper,
Router and conditional links. Bundled keys are `csharp` and `jsonata`; migrate earlier
uppercase spellings to those keys unless explicitly registered under another key. There is no type switch or
custom catalog. Additional engines can implement that same contract under another key.

MapperConfiguration is data-only. Registration snapshots settings and registers a
deferred component factory. Build resolves the compiler, reads the expression source,
compiles once and constructs Mapper. Invalid engine/file/syntax fails before Start,
and failed Build stops constructed components; provider teardown disposes them. Runtime data errors use
Mapper's existing FlowError path and preserve lineage. OutputCapacity defaults to 128;
other execution options retain component defaults in this configuration slice.

Expression accepts inline text or a path ending in `.jsonata`, `.csx` or `.cs`.
Fully qualified paths and explicit relative paths (`./` or `../`, including Windows
backslash forms) also work without a prefix, with any extension or no extension.
For example, `./maps/request.txt` and `./maps/request` both read files. A bare ambiguous
name such as `request` remains expression text; use `./request` to select the file.
Division and member access remain expressions rather than being inferred as paths.
For the built-in engines, `.jsonata` belongs to `jsonata`, and `.cs`/`.csx` belong
to `csharp`. Mapper and Router reject conflicting known extension/engine pairs at
Build with the component/route location. Engine remains explicitly selected; the
extension never selects it. Custom engines and other/absent extensions remain valid.
This check applies only to file sources, not inline or decoded Base64 text.
This rule is shared by Mapper expressions and Router route expressions. It does not
depend on whether a coincidentally named file exists. Missing recognized files fail
Build instead of falling back to expression text.
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
`{"Address":"sink.Input","Condition":"value > 0","Engine":"csharp"}`.
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
    var config = section.ReadRequiredConfiguration<MySinkConfiguration>();
    registrations.AddComponentFactory(address, provider => new MySink());
    registrations.AddInputPort<MySink>(address, "Input", node => node.Input);
    registrations.AddInputLinks(address.Port("Input"), config.Input);
});
services.AddFluxFlowApplication(configuration);
await using var provider = services.BuildServiceProvider();
await using var app = await provider.BuildApplicationAsync();
await app.StartAsync();
// Keep the application alive until host shutdown, then dispose it to drain work.
```

MySinkConfiguration is a data-only DTO with an Input property of type OneOrManyPortReferences,
defaulting to OneOrManyPortReferences.Empty.
Imports: FluxFlow.Configuration.Components.Scheduling, FluxFlow.Configuration.Application.Links, FluxFlow.Configuration.Application, FluxFlow.Configuration.Components,
FluxFlow.Application, and Microsoft.Extensions.DependencyInjection.
The host owns configuration providers and the service provider.

ComponentRegistration is registered as a keyed delegate instance by component type;
the last registration for a type is selected. It receives the standard service
collection and the exact component section. Registration failures do not partially
update the caller's service collection. Scheduler binds and validates settings here,
then captures them in its factory. Runtime dependencies are resolved by the factory.

AddComponentFactory registers a native keyed scoped IFlowNode under its unique
workflow.component address; its synchronous delegate is invoked lazily by DI.
There is no separate keyed ComponentFactory service. Configuration checks the actual
deferred scoped IFlowNode descriptor without invoking it.
Workflows are keyed FlowWorkflow singletons with an unkeyed enumeration alias.
ComponentAddress values are registered under the workflow name for discovery without
resolving components. No workflow-name dictionary or runtime component lookup is built.
Configuration registration does not build a provider. AddFlowWorkflow
can preserve an empty workflow. AddInputPort, AddOutputPort and AddEventPort register
native keyed blocks, independently of construction. Each selector resolves the canonical
component and runs once per workflow run. Named events resolve the address-stamped bridge output.

BuildApplicationAsync composes the provider's single FlowApplication: it resolves workflows,
resolves keyed components and blocks in their workflow scopes, invokes LinkFactory delegates and
validates links. It returns an unstarted application. Repeated Build calls return the same
Application and do not rerun factories or link delegates. LinkFactory receives the original
host provider; there is no temporary port provider or separate ordinary resource provider.
StartAsync connects prepared routes using those existing keyed blocks and starts sources;
it constructs no components. Build checks cancellation between synchronous factories
and stops partial construction on failure. DI owns disposal, including nodes constructed
before a Build failure. Constructors must not perform long-running I/O; source startup is
separate. One Application runs per host provider. Component factories resolve dependencies
from their workflow's native DI scope. Shared application resources remain root singletons;
workflow-local resource factories are scoped. Independent workflow restart creates a new
scope from unchanged registrations; configuration is not read again. See
[workflow lifetime](../FluxFlow.Application/README.md#workflow-stop-and-restart).

For example, a link registration connects native blocks by address:

```csharp
services.AddSingleton<LinkFactory>(ports =>
{
    ports.Link(PortAddress.Parse("demo.clock.Output"), PortAddress.Parse("demo.sink.Input"));
});
```

Link accepts full addresses and an optional native C# predicate. A false predicate
filters the message from that link. Every matching link receives it; no matches
means the message is consumed without delivery or routing failure. Registered outputs
with no links also drain normally. There is no Fallback field or LinkFallback API.
Links use native LinkTo predicates. Broadcast outputs do not wait for destination
capacity: full or declining consumers may miss messages. Thrown conditions fault
execution. Delivery/rejection callbacks are not link options. Fan-in completion
coordination remains separate from native broadcast publication. Native
ISourceBlock/ITargetBlock instances are available by the same keys through
`provider.GetWorkflowServices(workflowName)`. Host dependencies are
available from that same provider; registrations are never copied into a second provider.
Components receive explicit dependencies, not the provider used for composition.

Stop/dispose Application before disposing the host provider. Application drains execution;
workflow scopes dispose components and local resources, and root DI retains shared resources.
Standard DI disposal stops on the first disposal
failure; FluxFlow adds no aggregate cleanup manager. A factory cannot return a node already
registered under another key/provider. A constructor that throws before returning must
clean up resources it acquired outside DI.

Declared port sections are read directly, preserving arrays,
conditions and custom port names. Each parsed link
registers its own LinkFactory. No temporary JSON link document or application-wide
JSON scan occurs. Put constants directly in Condition; message-dependent values come
from the standard value, headers and message-context variables. The former Parameters
field is rejected. Conditions compile once per engine/text pair within the provider's
single compiler instance, including reuse of compilation failures. Engines resolve through
native DI; message evaluation is never cached.
Resource sections invoke a type-keyed
`FluxFlow.Configuration.Application.ResourceRegistration(IServiceCollection, ApplicationAddress,
IConfigurationSection)` during AddFluxFlowApplication. That callback registers
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
registration callbacks or creating a resource provider. DI owns resource instances within
their application or workflow scope. Externally supplied instances remain caller-owned.

FlowApplication coordinates component resolution and link declarations internally;
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
`FluxFlow.Configuration.Components.Routing` and `AddSource()` from
`FluxFlow.Configuration.Components.Sources` alongside `AddScheduler()`.

```json
{
  "workflows": {
    "demo": {
      "clock": { "type": "scheduler", "interval": "00:00:05", "output": "samples.Input" },
      "samples": { "type": "source", "values": "[123,\"123\",true,null,{\"code\":\"001\"}]", "output": "gate.Input" },
      "gate": { "type": "gate", "initiallyOpen": true, "output": "sink.Input" },
      "sink": { "type": "my_sink" }
    }
  }
}
```

Gate defaults to closed. It registers Input, Open, Close, Output and Events; Open/Close
are ordinary command inputs, not terminal reply inputs. Closed gates discard normal and error messages. InitiallyOpen
and positive OutputCapacity (default 128) are configuration settings; other native
execution options retain their component defaults in this initial registration.

Source emits one sample for each ordinary Input, in order. It does not schedule
itself, repeat the list or emit after exhaustion. Values is a required `JsonSource`
containing an array; an empty array is valid. OutputCapacity defaults to 128 and must
be positive. Parsing occurs once at registration; immutable values survive disposal
of the temporary JsonDocument. Each workflow run owns one Source instance and its cursor;
repeated Build reuses it, while workflow stop/restart starts the sample sequence again.

Using JSON text here is deliberate: IConfiguration exposes string values and cannot
preserve the original distinction between `123` and `"123"` in a heterogeneous array.
Values preserves that distinction, nested objects/arrays and null with standard
System.Text.Json. Quotes inside inline text need JSON escaping as shown. Larger samples
can live in a separate JSON file:

```json
"samples": {
  "Type": "source",
  "Values": "samples/orders.json",
  "Output": "send.Input"
}
```

Register `services.AddSource(baseDirectory: hostEnvironment.ContentRootPath)` to select
the directory for relative file paths; the default is AppContext.BaseDirectory.
`JsonSource` accepts plain JSON text or a file path without a required prefix.
`inline:` and `file:` are optional explicit overrides. Valid JSON is read as JSON
first, so `123`, `true`, `null` and quoted strings retain their types even if a file
has the same name. For such filenames use `file:123` or an unambiguous path such as
`./123`. Text beginning with `{`, `[` or a quote is treated as JSON even if malformed;
other text that is not valid JSON is read as a file path. Relative/absolute paths,
spaces and extensionless files are supported. Quoting a path *inside* the JSON text
makes it a JSON string rather than a file reference.
It binds with the native configuration binder and returns an independent JsonElement
from Read(baseDirectory). It accepts any JSON value; the Source adapter requires an
array. Files are read and validated during AddFluxFlowApplication, just like
inline samples. Values are snapshotted for deferred construction, so changing or
removing a file after registration does not change this application. This intentionally
differs from Mapper/Router expression files, which are read at Build.

Missing files and malformed/non-array data fail registration with the component
address; the caller's services remain unchanged. There is no extension restriction,
recursive source interpretation, Base64 support or automatic reload here.
Migration: rename the old `ValuesJson` property to `Values`; existing inline JSON
text needs no other change. The old property is removed rather than retained as an alias.

Configuration references Application and all ten component packages.
Component projects remain independent of Configuration. DTOs contain data only;
registration methods map them to constructor arguments/options and native port selectors.

## Setup coverage

Every current component has an explicit registration extension:

| Component | Registration | Namespace under FluxFlow.Configuration.Components |
| --- | --- | --- |
| Batch | AddBatch | Batching |
| ChangeFilter | AddChangeFilter | Filtering |
| Deduplicate | AddDeduplicate | Filtering |
| Mapper | AddMapper | Mapping |
| RateLimit | AddRateLimit | RateLimiting |
| Reducer | AddReducer | Reducing |
| Retry | AddRetry | Resilience |
| Gate | AddGate | Routing |
| Router | AddRouter | Routing |
| Scheduler | AddScheduler | Scheduling |
| Source | AddSource | Sources |

All adapters register Output and Events and support the configuration-only Error
selection. All except Scheduler have Input. Retry and RateLimit also expose Request
and Reply; these links use the same typed references, conditions and duplicate handling.
Native execution options retain each component's defaults.

### ChangeFilter and Deduplicate

Register AddChangeFilter / AddDeduplicate and the chosen expression engine. Engine
and KeySelector are required. The selector returns a non-null string key using the
usual value, headers, messageId, traceId and timestamp expression context. Convert
numeric or compound keys explicitly in the expression. Keys compare ordinally;
IgnoreCase optionally uses ordinal case-insensitive comparison. Runtime components
remain generic for code-authored use.

```json
{
  "changes": {
    "Type": "changeFilter", "Engine": "jsonata", "KeySelector": "$string(status)",
    "Output": "unique.Input"
  },
  "unique": {
    "Type": "deduplicate", "Engine": "csharp", "KeySelector": "headers.eventId",
    "Retention": "00:05:00", "MaxKeys": 1024, "Output": "send.Input"
  }
}
```

ChangeFilter remembers the last forwarded key; Deduplicate remembers keys for a
positive Retention, bounded by positive MaxKeys (default 1024). Deduplicate resolves
the host TimeProvider, or uses System. OutputCapacity defaults to 128 and must be positive.
Both preserve the original message and forward errors without changing remembered keys.

### Reducer

Register AddReducer. InitialState is a JsonSource read and snapshotted during
registration, like Source.Values. Engine, Reduce and AreEqual are required.

```json
"total": {
  "Type": "reducer", "Engine": "csharp", "InitialState": "0",
  "Reduce": "state + input", "AreEqual": "previous == current",
  "Output": "send.Input"
}
```

Reduce sees state and input (also aliased as value/payload) and returns the next
FlowValue. AreEqual sees previous and current and returns a boolean. JSONata uses
$state, $input, $previous and $current. These are value-only callbacks, matching
ReducerOptions; they do not receive an envelope or fabricated message metadata.
Equal candidates neither replace state nor emit output. Changed states retain
the triggering message's lineage and headers. Each provider has independent state.

AddReducer(baseDirectory) resolves both initial-state and expression paths relative
to that directory. Other expression adapters accept expressionBaseDirectory.
ExpressionSource supports inline text, file paths and explicit Base64. Expressions
are read and compiled during Build, before Start, and reused for each message.

### RateLimit

Register AddRateLimit. Limiter is a required native RateLimiter resource reference;
it can name a local workflow resource, an application resource or an existing host
key. The host provider constructs and owns the limiter; the component borrows it.
This supports all native limiter implementations, including shared limiters.

```json
"limit": {
  "Type": "rateLimit", "Limiter": "outbound",
  "ResponseTimeout": "00:00:30",
  "Request": "send.Input", "Reply": "send.Output", "Output": "finished.Input"
}
```

For example, the host can register a shared native limiter directly:

```csharp
services.AddKeyedSingleton<RateLimiter>("outbound", (_, _) =>
    new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 128 }));
services.AddRateLimit();
```

To put its settings in Resources, register the existing ResourceRegistration delegate
under a type key, bind that resource's native options, and call AddFluxFlowResource:

```csharp
services.AddKeyedSingleton<ResourceRegistration>("concurrency", (registrations, address, section) =>
{
    var options = section.ReadRequiredConfiguration<ConcurrencyLimiterOptions>();
    registrations.AddFluxFlowResource<RateLimiter>(address, _ => new ConcurrencyLimiter(options));
});
```

Then Resources.outbound can contain Type = concurrency, PermitLimit and QueueLimit.
Fixed-window, sliding-window and token-bucket resources follow the same native pattern.
There is no second limiter implementation in Configuration.
ResponseTimeout covers request admission and reply waiting after acquiring a lease;
it defaults to infinite (-00:00:00.001). A lease remains held until reply/failure.

### Retry

Register AddRetry. Request sends each attempt; Reply receives correlated results.
Output publishes the final result with the caller's correlation restored.

```json
"retry": {
  "Type": "retry", "MaxRetryAttempts": 3, "Delay": "00:00:02",
  "BackoffType": "Exponential", "UseJitter": true,
  "AttemptTimeout": "00:00:30",
  "Request": "send.Input", "Reply": "send.Output", "Output": "finished.Input"
}
```

Defaults match RetryOptions: three retries, two-second constant delay, no jitter,
no maximum delay and a thirty-second attempt deadline. MaxDelay is optional.
The default policy retries transient FlowError replies. Optional ShouldRetry is an
ExpressionSource requiring an explicit Engine; it replaces that reply predicate.
It receives the complete reply context, for example:
`message.IsError && value.Code == "busy"` with csharp, or
`Code = 'busy'` with jsonata. The predicate also sees successful replies, so guard
error-only property access. Transport exceptions/timeouts are not retried by this
reply predicate; code-authored native policies remain available for those cases.
No engine is needed when using the default policy.

Both request/reply adapters use positive OutputCapacity (default 128). Deadlines
must be positive and within the runtime timer limit, or explicitly infinite.
Policy fields bind to the existing native resilience options; no custom retry
engine or delegate-in-configuration model is introduced.
