# FluxFlow.Composition

Canonical application definitions, explicit component registration, addressing,
validation, link compilation, and code-first runtime ownership. The package is
Engine-independent.

## Application Shape

```json
{
  "Resources": {},
  "Workflows": {
    "Orders": {
      "Receive": { "Type": "source" },
      "Handle": {
        "Type": "handler",
        "Input": "Receive.Output"
      }
    }
  }
}
```

Resources, workflows, and components are named by object keys. Components are
flat; there are no maintained Composition, Nodes, or root Links wrappers.
Addresses are ordinal and case-sensitive. Links support fan-in, fan-out,
conditions, cross-workflow addresses, and explicit bounded signal feedback.
Ordinary data-processing cycles are rejected.

## Code-First Authoring

JSON/configuration and compiled C# are independent authoring sources. They
converge at normalization, validation, link compilation, and runtime execution;
the C# builder does not serialize or round-trip through JSON.

Create workflow scopes either by return value or by flat fluent capture:

```csharp
var application = new ApplicationDefinitionBuilder()
    .AddWorkflow("Orders", out var orders)
    .AddWorkflow("Audit", out var audit);

orders
    .AddComponent("Source", OrderComponents.Source, out var source)
    .AddComponent("Review", OrderComponents.Review, out var review)
    .AddComponent("Priority", OrderComponents.Sink, out var priority)
    .AddComponent("Standard", OrderComponents.Sink, out var standard);

audit.AddComponent("Events", OrderComponents.EventCollector, out var events);

source.Output.ConnectTo(review.Input);
review.Output
    .ConnectTo(priority.Input, when: static order => order.Priority)
    .ConnectTo(standard.Input, when: static order => !order.Priority);
review.Events.ConnectTo(events.Input);

var definition = application.Build();
services.AddFluxFlow(definition);
```

`ComponentContract<THandle>` and `ComponentContract<TOptions,THandle>` hold one
complete declaration: canonical type, runtime factory and bindings, typed
handle, and optional component-specific options factory/apply delegate. Contract
construction creates one immutable descriptor but performs no reflection,
scanning, service resolution, or runtime activation. Official packages expose
contracts through `<Family>Components` classes, and their retained `AddX`
methods delegate to the same core. Typed handles expose named ports, including
explicit `Events` outputs.

`ConnectTo` returns the same output handle for fan-out. Workflow `Connect` is
local; application `Connect` and direct `ConnectTo` support same-owner
cross-workflow links. Conditions may be portable expression strings or
synchronous `Func<T,bool>` predicates. Predicates are definition/revision-owned,
require no expression engine, skip error messages, and isolate exceptions to
the affected route.

`Build()` freezes the graph and returns a directly hostable in-memory
`ApplicationDefinition` that owns the exact runtime descriptors introduced by
its complete component contracts and the exact executable registrars introduced
by its application resource contracts. `services.AddFluxFlow(definition)`
therefore needs no duplicate component or resource-family registration. The
UI/designer remains JSON-only, and C# export or serialization is intentionally
not part of this API. See
`docs/39-typed-code-first-authoring.md` for custom contracts, condition and
revision semantics, and migration guidance.

`ApplicationLinkCompiler` owns parsing, address resolution, validation,
normalization, and deterministic ordering for both sources. Its result exposes
executable `Links` plus resolved portable `Declarations` for JSON persistence.
Code predicates remain executable in-memory links and are not persistence
projections. Serialize edited
`ApplicationLinkDeclarationProjection` values with
`ApplicationLinkCompiler.SerializeDeclarations(...)` so hosts and Designer use
the same exact `Port` / `Condition` grammar. Composition grants no production
friend access to Designer or Engine.

`ComponentDescriptor` declares one canonical type, typed
`FlowMessage<T>` ports, link cardinality, processing capabilities, and an
activation delegate. For a JSON or low-level string definition, explicitly
register a reusable contract with `AddComponent(contract)`. Author a dynamic
runtime-only descriptor through the explicit
`AddFluxFlowComponents().Advanced.AddDynamicComponent(...)` escape hatch.
Engine builds one effective immutable `ComponentCatalog` per candidate from
host registrations plus definition-owned descriptors; application validation,
link compilation, activation, and Designer metadata consume the same descriptor
facts.
Errors travel on normal outputs. Application revisions own component and link
lifecycle but do not own external resources supplied by the host.

```csharp
services.AddFluxFlowComponents()
    .Advanced
    .AddDynamicComponent("orders.handle", component =>
    {
        component
            .UseFactory(CreateHandler)
            .HasInput("Input", static node => node.Input)
            .HasOutput("Output", static node => node.Output)
            .HasEvents("Events", static node => node.Events);
    });
```

This is the advanced/dynamic path, not an additional step after typed
`AddComponent(name, contract, ...)` authoring. `UseFactory` accepts synchronous
node factories and asynchronous `ValueTask`
node factories. Its typed builder makes each port declaration authoritative for
both descriptor metadata and the activated Dataflow binding. Selectors run only
after activation. `HasInput`, `HasSignalInput`, `HasOutput`, and `HasEvents`
describe existing node members; they do not create duplicate Dataflow ports.
`HasEvents(name, selector)` explicitly bridges the selected
`FlowEvent` source to a public `ComponentEvent` output; event ports are never
injected, and `Events` is not reserved. Advanced complete-instance factories
use `UseInstanceFactory`, while `ComponentNodeActivation<TNode>` carries the
small optional completion/additional-cleanup case without duplicating ports.

Composition adapters that materialize application resources implement
`IApplicationResourceRegistrar`. Its context exposes the complete definition,
revision identity, host services, and revision-owned `IServiceCollection`.
`ApplicationResourceContract<THandle>` and
`ApplicationResourceContract<TOptions,THandle>` pair one portable resource type,
typed handle, explicit options projection, and registrar. Contracts are kept in
`ApplicationDefinition.ApplicationResourceContracts` for compiled C# only and
are excluded from canonical JSON. Exact contract/registrar reuse deduplicates;
different contracts for one type fail atomically before activation.
Canonical keyed DI helpers live in
`FluxFlow.Composition.DependencyInjection`; Engine consumes these low-level
contracts without making adapters depend on a hosting package.

Canonical workflow JSON selects an optional semantic `Processing` profile.
Composition maps that profile centrally to capacity, parallelism, and ordering.
Direct C# callers may still provide the technical options explicitly; those
compatibility settings are not primary workflow or Designer concepts.

`ApplicationRuntime` waits for all upstreams before completing a shared input,
faults fan-in once on the first upstream fault, and attempts all cleanup before
aggregating teardown failures.
