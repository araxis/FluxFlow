# FluxFlow.Expressions.CSharp

AddCSharpExpressionEngine registers keyed IFlowExpressionEngine for both Mapper and
conditional links. Use `services.AddCSharpExpressionEngine(useAsDefault: true)` for
links that omit Engine; explicitly named engines still resolve by that key without
fallback. Mapper and Router require their own Engine. There is no application/workflow
ExpressionEngine setting. The optional flag defaults to false; if multiple engines
are explicitly selected as default, the last registration wins under normal .NET DI
rules. Named and default services share the same stateless engine instance.
Bound condition parameters and input/payload aliases support the same lazy JSON
access as value. Predicates must return bool. The package depends only on Core.

Optional DynamicExpresso.Core 2.19.3 integration. Core and Mapper do not depend on
this package. Register `services.AddCSharpExpressionEngine()` and `services.AddMapper()`;
configure the mapper with `"engine": "csharp"` and e.g.
`"expression": "value.items[0].quantity * 2"`.

`CSharpExpressions.Compile(text)` can also be reused by setup code directly. It returns
a normal message mapping delegate; no catalog, interpreter context or component
engine interface is required. Each Build compiles once for that component.

Expression parameters: `value`, `headers`, `messageId`, `traceId`, `correlationId`,
`causationId`, `timestamp`. IDs are strings; absent optional IDs are null.
Timestamp is DateTimeOffset, and DateTimeOffset/TimeSpan are available expression types.
Built-in message parameters also have PascalCase aliases: `Input`, `Payload`, `Value`,
`Message`, `Headers`, `MessageId`, `TraceId`, `CorrelationId`, `CausationId` and
`Timestamp`. Their camelCase forms remain supported. Input/header property names
and caller-supplied variables retain their exact casing.
Value and nested header values use each FlowValue's cached JSON representation.
Objects/arrays are read-only dynamic views, with dot access and indexing. Keys are
case-sensitive; `headers["site-code"]` handles non-identifier keys. Missing fields and
invalid array indexes throw. JSON null is null; strings are never inferred as numbers
or dates. Arrays returned by an expression remain one Mapper output message.

JSON integers use Int64; other representable numbers use Decimal, then finite Double.
Numeric literals default to Decimal to make ordinary fractional arithmetic compatible
with JSON decimals. Integral decimal indexes are accepted; fractional indexes fail.
Explicit double literals/casts use normal CLR rules and may require casting the operand.
Double fallback is approximate, not arbitrary precision. Whole JSON objects/arrays retain
their original number tokens when returned directly or nested in the result.

Results are serialized into owned JsonFlowValue values. A small converter writes dynamic
views as their underlying JSON (including when nested in arrays/CLR results), not `{}`.
No reflection adapter or eager dictionary tree is built for inputs.

This is DynamicExpresso's C# expression subset, not a Roslyn script host. For example,
object array initializers containing value types may require explicit `(object)` casts.
Do not assume anonymous object construction, arbitrary statements or LINQ over dynamic
JSON arrays. Use JSONata for rich JSON shaping, or a code-supplied mapping delegate.
Compiled invocations have per-message arguments and no mutable interpreter variables.
Cancellation is checked before/after evaluation; synchronous engine code is not
preemptible mid-expression. Expressions are trusted application configuration, not a sandbox.

Syntax errors fail Build; unknown dynamic properties/types fail on the relevant message
and use Mapper's existing FlowError handling. See Configuration README for inline/file rules.
