# Component Composition

Component packages are meant to be composed by an application host. The engine
builds and runs the graph; packages provide reusable node behavior; the host
adapts resources, storage, expressions, and app-specific models.

## Recommended Path

Start with a small graph and add component families only when they remove real
host code:

1. Keep source and sink nodes in the host until the behavior is clearly reusable.
2. Add `source.generated` or `source.sequence` for deterministic generated
   streams that do not depend on a transport or app store.
3. Add `flow.mapper` to translate from host input shapes into package request
   contracts.
4. Add `flow.filter`, `flow.when`, or `flow.switch` for expression-driven
   decisions.
5. Add `flow.fork` when several branches must receive every input.
6. Add `flow.merge` when same-type streams need to converge with source
   metadata.
7. Add `flow.assert` when a flow needs assertion results or pass/fail streams.
8. Add `flow.correlation` when a single stream needs request/response pairing
   by key.
9. Add `flow.join` when two streams need to be paired by related keys.
10. Add `state.reducer` when later decisions depend on previous messages.
11. Add `flow.counter`, `flow.metrics`, or `flow.logger` when a stream needs
   runtime observation.
12. Add `timer.interval`, `timer.schedule`, `timer.delay`, `timer.throttle`, or
   `timer.debounce` when time is part of the flow.
13. Add edge packages for validation, serialization, payload inspection, HTTP,
   file system operations, recording, replay, storage, or external transport
   adapters.

This keeps early application work direct while leaving a clean path to extract
generic behavior later.

## Host Boundary

The host should own:

- resource lookup and connection names
- concrete clients and storage implementations
- expression engine registration
- app-specific input/output models
- workspace projection into `ApplicationDefinition`
- dashboard, designer, and activity projection
- source and sink nodes that are still product-specific

The host should avoid putting reusable processing logic inside adapters. If an
adapter starts to parse, validate, route, aggregate, or persist in a generic
way, it is probably hiding a future component package.

## Package Boundary

A component package should own:

- neutral request/result contracts
- options and option validation
- typed ports and bounded capacity
- per-message failures as `FlowError` where continuation is expected
- startup failures when the node cannot begin safely
- diagnostics and optional workflow events
- lifecycle, completion, cancellation, and disposal behavior
- tests that do not require a product host

Packages should not own:

- app workspace models
- dashboard layout or UI projection
- product-specific names, scenarios, or storage paths
- concrete external clients when a host adapter is reasonable
- assumptions about how another app names sections or resources

## Common Shapes

Use request/result nodes when behavior acts like a function:

```text
Input:  SomeRequest
Output: SomeResult
Errors: FlowError
```

Use source nodes when the package starts a stream:

```text
Output: SomeMessage
Errors: FlowError when recoverable
Startup failure when the source cannot start
```

Use observer nodes when the package watches a stream without changing it:

```text
Input:     Any registered type
Snapshots: Counter, metric, or log entry contract
```

For each shape, prefer typed contracts over loosely shaped dictionaries. Hosts
can map app data into those contracts with `flow.mapper`.

## Composition Examples

Stateful timer flow:

```text
timer.interval -> flow.mapper -> state.reducer -> flow.counter
                                  |
                                  +-> host sink
```

Deterministic source flow:

```text
source.generated -> flow.mapper -> flow.assert -> host sink
```

Validation and routing:

```text
host source -> flow.mapper -> json.schema-validator -> flow.switch -> host sinks
```

Switch with direct route outputs:

```text
host source -> flow.switch
                  |-> Priority -> host sink
                  |-> Standard -> host sink
```

Switch with a route envelope:

```text
host source -> flow.switch.Routed -> flow.mapper -> host sink
```

Reliable fan-out:

```text
host source -> flow.fork
                  |-> Audit -> host sink
                  |-> Work  -> flow.mapper -> host sink
```

Source-tagged merge:

```text
primary source -> flow.merge -> flow.assert -> host sink
replay source  ->/
```

Request/response pairing:

```text
host source -> flow.correlation -> flow.assert -> host sink
```

Windowed processing:

```text
host source -> flow.window -> flow.mapper -> host sink
```

Two-stream join:

```text
left source  -> flow.join -> flow.assert -> host sink
right source ->/
```

Recording and replay:

```text
host source -> flow.mapper -> session.recorder -> host sink

session.replay -> flow.mapper -> host sink
```

Transport boundary:

```text
transport source -> flow.mapper -> flow.when -> transport publisher
```

The transport package should only know its own request/result contracts. The
application decides how workspace resources map to adapter instances.

## Extraction Checklist

Create a new component package when most of these are true:

- the behavior is useful in more than one application
- the contracts can be named without product language
- the node can run with injected adapters or in-memory fakes
- the host only needs small mappers or context factories
- tests can cover behavior without a dashboard or workspace store
- options and diagnostics are stable enough to document

Keep the behavior in the host when:

- it depends on product UI, dashboard, or scenario rules
- it depends on app-specific storage shape
- it is still changing because the product workflow is unclear
- it cannot be tested without the full product runtime

## Release Impact

Adding a component package should not require an engine release unless the
engine contract itself changes. A package can move independently when it only
adds nodes, contracts, options, tests, and docs under its own project.
