# FluxFlow.Expressions.Jsonata

Optional Jsonata.Net.Native 3.0.0 / SystemTextJson integration. Register
`services.AddJsonataExpressionEngine()` and `services.AddMapper()`; configure
`"Engine": "jsonata"` and e.g. `"Expression": "{'body': $payload, 'id': $messageId}"`.
Core and Mapper do not reference the engine. `JsonataExpressions.Compile(text)` is
also reusable by setup code and returns a normal message mapping delegate.
The registration supplies keyed `IFlowExpressionEngine` for both Mapper and conditional
links. Use `services.AddJsonataExpressionEngine(useAsDefault: true)` to select JSONata
for links that omit Engine. An explicit link `"Engine": "jsonata"` always selects
that key; missing explicit keys fail rather than falling back. Mapper and Router
still require their own Engine. There is no application/workflow ExpressionEngine
setting. The optional flag defaults to false; if multiple engines are explicitly
selected as default, the last registration wins under normal .NET DI rules.
The named and default services share the same stateless engine instance. Conditions may
use their bound parameters and `$input`/`$payload` aliases. Conditional results must be
booleans; strings/numbers/null are not coerced. The package depends only on Core.

The payload JSON is the JSONata context (`$`), and is also bound as `$value`.
Independent bindings are `$headers`, `$messageId`, `$traceId`, `$correlationId`,
`$causationId`, `$timestamp`. IDs are strings; absent IDs are null. Timestamp uses
invariant ISO 8601 text because JSONata has no CLR DateTimeOffset type.

Every built-in message binding has camelCase and PascalCase aliases: `$input`/`$Input`,
`$payload`/`$Payload`, `$value`/`$Value`, `$message`/`$Message`, `$headers`/`$Headers`,
`$messageId`/`$MessageId`, `$traceId`/`$TraceId`, `$correlationId`/`$CorrelationId`,
`$causationId`/`$CausationId` and `$timestamp`/`$Timestamp`. This does not rename
input properties or header keys, make JSONata case-insensitive, or add aliases to
caller-supplied variables. `Reading` and `$.Reading` access the input property;
`$Reading` is a separate variable.

The immutable query is parsed once per Build/component. Each invocation creates its own
environment and input DOM from FlowValue.Json using the library's SystemTextJson bridge.
Headers use each header FlowValue's JSON. Output uses the library's ToSystemTextJson
bridge and an owned JsonFlowValue; that output bridge currently serializes text and
parses a JsonDocument. Do not claim zero-copy mapping. No global query cache is added.

JSONata language semantics remain intact, including sequences and missing fields.
A top-level undefined result is rejected as a processing error; explicit null is a
valid payload. Arrays remain one Mapper output, not multiple messages. Syntax errors
fail Build; runtime errors use the existing FlowError output. Cancellation is checked
before/after synchronous evaluation, not during it. Expressions are trusted configuration.
See Configuration README for shared inline/file rules.
