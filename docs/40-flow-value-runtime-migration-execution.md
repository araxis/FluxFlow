# FlowValue Runtime Migration Execution Prompt

## Role

Act as the senior engineer responsible for completing FluxFlow's canonical
runtime-value migration. Execute the work in the ordered slices below. Do not
stop at compatibility adapters or partial registration changes.

## Objective

Make every dynamically authored workflow data port physically carry the
canonical `FlowValue` payload. Each visible component must materialize its own
private domain input and canonicalize its own output. The final framework and
engine transport must use the non-generic `FlowMessage` envelope. Generic
messages may return later only as optional code-first authoring helpers outside
the runtime contract.

## Non-negotiable invariants

1. Never introduce a global materializer, mapping registry, service locator, or
   God object.
2. Never insert an implicit Dataflow block between two components. The runtime
   graph, designer graph, logs, events, and user mental model must describe the
   same visible components.
3. A component owns `IFlowValueMaterializer<TInternalInput>` for the internal
   type it needs. The materializer remains in that component's module.
4. Materialization failure is normal workflow data: return a module-owned
   `FlowError`, emit a rejection event from the visible component, do not invoke
   the underlying operation, and do not fault the pipeline.
5. Successful outputs are converted to `FlowValue` inside the visible
   component. Preserve message, correlation, trace, causation, headers, and
   timestamp semantics through `With`/canonical message operations.
6. Intentional JSON null is `FlowValue.Null`; successful runtime messages do
   not carry a null `FlowValue` reference.
7. Signals and diagnostic events keep their dedicated contracts. Only workflow
   data ports use `FlowValue`.
8. Designer input-shape metadata comes from the component-owned materializer.
   Registration must not execute materialization.
9. Preserve bounded capacity, ordering, concurrency, cancellation, completion,
   fan-out, disposal, and transport/resource ownership behavior.
10. Do not weaken domain validation or convert expected operational failures
    into thrown exceptions.

## Per-component execution algorithm

For every dynamically registered component with a typed data port:

1. Identify every physical input and output block and its registration.
2. Change physical data ports to `FlowMessage<FlowValue>` during the component
   migration stage.
3. Inject or construct the module-owned materializer inside the node. Optional
   constructor injection is allowed for deterministic testing.
4. At input processing start, propagate an incoming error unchanged.
5. Materialize the successful `FlowValue` into the private domain request.
6. On rejection, emit `<module>.<operation>.rejected`, include the module-owned
   error code, return `WithError<FlowValue>`, and continue accepting messages.
7. Execute all existing domain and transport behavior using the materialized
   request.
8. Convert successful domain output with `FlowValue.From(...)` before emission.
9. Change registration to the exact direct `HasFlowValueInput` or
   `HasFlowValueOutput` overload. Pass only `materializer.InputShape` for input
   design metadata.
10. Update runtime tests, composition tests, examples, and public API baseline.

## Ordered implementation slices

### Slice 1: FileSystem and Storage

Migrate file read, file write, directory enumeration, file watch, storage put,
storage get, storage query, and storage delete nodes. Keep exact `FlowContent`
bytes and metadata intact. Verify missing/not-found outcomes, receipts, queries,
source fan-out, and file-change events remain canonical values.

### Slice 2: Sessions

Migrate session recorder, query, and replay paths. Preserve session identity,
ordering, stored exact content, replay behavior, and normal failure results.

### Slice 3: State, Metrics, Projections, and Expectations

Migrate each module independently while keeping its materializer local. Preserve
state revision/concurrency behavior, metric aggregation windows, projection
folding, expectation timeout/completion behavior, and deterministic clocks.

### Slice 4: Remaining typed dynamic components

Use compilation and registration searches to find any typed runtime-authored
data port not covered above. Treat HTTP server request/reply ingress as a
separate correlated boundary: migrate it deliberately without collapsing its
request/reply coordination or signal semantics.

### Slice 5: Remove hidden adapter infrastructure

After every component has physical value ports:

1. Delete `FlowValuePortAdapter`.
2. Delete adapter-producing generic `HasFlowValueInput<T>` and
   `HasFlowValueOutput<T>` overloads.
3. Keep only exact direct value-port registration plus explicit typed code-first
   APIs whose names do not imply dynamic runtime compatibility.
4. Use compiler failures and repository searches to identify missed callers.
5. Assert the public DSL method surface explicitly in composition and designer
   tests.

### Slice 6: Non-generic runtime envelope

Replace `FlowMessage<FlowValue>` with non-generic `FlowMessage` across Nodes,
Composition, Engine, designer/runtime bindings, component ports, application
ports, persistence/restore paths, mapper/expression contexts, and tests.

The non-generic envelope must expose:

- flat script-facing `IsError`, `Value`, and `Error` projections;
- private construction that enforces success/error invariants;
- `TryGetValue`, `TryGetError`, and `Match` for safe C# consumption;
- non-throwing script inspection of the inactive projection;
- `FlowValue.Null` for successful JSON null;
- lineage-preserving `With`, `WithError`, restore, and serialization behavior.

Delete framework usage of `FlowMessage<T>` after migration. If generic
authoring convenience is still desired, design it later as a boundary helper
that converts before entering the runtime and never changes runtime port types.

## Required tests per migrated node

1. A representative valid duck-typed object is materialized and processed.
2. A malformed scalar or object returns the module-owned error code.
3. Rejection emits an event from the visible node with correlation and error
   code.
4. The rejected message does not invoke the underlying transport/store/action.
5. A valid message after rejection succeeds and the node remains unfaulted.
6. Successful output is script-readable canonical JSON.
7. Correlation, trace, causation, message identity, and headers remain correct.
8. Existing concurrency, fan-out, cancellation, completion, and disposal tests
   continue to pass.
9. Dynamic composition sends and receives the physical canonical value type.
10. Input-shape metadata remains discoverable to designers and validators.

## Validation gates

For each slice:

1. Run the affected runtime unit projects.
2. Run the affected composition projects.
3. Run shared Composition and Designer tests when registration APIs change.
4. Review and accept the public API baseline only for deliberate changes.
5. Run the complete release test project.

Before deleting adapters and generic messages, search the source tree and require
zero dynamic component registrations that depend on typed value adapters.

Final completion requires:

- no hidden value adapter block;
- no dynamic component physical data port with a domain payload type;
- no framework or engine dependency on `FlowMessage<T>`;
- all focused and release tests passing with zero warnings;
- documentation and examples showing the same physical contracts as runtime;
- a compact requirement-to-test evidence table in the implementation report.
