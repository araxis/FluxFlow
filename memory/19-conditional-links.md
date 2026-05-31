# Conditional Links

Date: 2026-05-31

## Decision

`LinkDefinition.When` is now runtime behavior, not only metadata.

When a link has a `when` expression, the runtime evaluates that expression for
each output item before sending the item to the target input. If the predicate
returns `false`, only that target is skipped. Other linked targets still receive
the item when their own predicates match.

## Runtime Shape

- `ApplicationRuntimeBuilder` creates an expression-backed predicate for each
  link with a non-empty `when`.
- `OutputPort.TryLinkTo` has an overload that accepts an optional predicate.
- `OutputPort<T>` evaluates that predicate per linked target during fanout.
- Unconditional links still use the existing API and behavior.

## Expression Context

The default condition context exposes the current output item as:

- `input`
- `value`

This keeps conditions short in definitions:

```json
{ "from": "source.Output", "when": "input % 2 == 0" }
```

## Notes

This belongs in the engine because conditional routing is graph behavior, not a
component concern. Component packages can keep exposing normal typed inputs and
outputs; the runtime applies the condition while wiring the graph.

This is planned for `0.4.0-alpha.1`.
