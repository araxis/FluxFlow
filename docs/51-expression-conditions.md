# Configurable conditions

C#-only workflows can keep using `when:` delegates. Expression conditions are
primarily for saved documents; configuration is optional, not part of every link.

```json
{
  "ExpressionEngine": "standard",
  "Resources": {},
  "Workflows": {
    "Main": {
      "ExpressionEngine": "rules",
      "Source": {
        "Type": "sample.source",
        "Output": {
          "Port": "Sink.Input",
          "Condition": "above(input, minimum)",
          "Parameters": { "minimum": 10 }
        }
      },
      "Sink": { "Type": "sample.sink" }
    }
  }
}
```

Component types/ports must be registered by the host. This example assumes a
canonical value containing an integer and a host-configured `above(FlowValue,
int)` helper. Component transport remains canonical; choosing an expression
engine does not change it.

## Host configuration

Register ordinary keyed `IFlowExpressionEngine` services. Keys are host-chosen
names, not a fixed set of languages or another library-owned registry:

```csharp
services.AddKeyedSingleton<IFlowExpressionEngine>("standard",
    new JsonataFlowExpressionEngine());

services.AddKeyedSingleton<IFlowExpressionEngine>("rules",
    new CSharpFlowExpressionEngine(interpreter =>
    {
        interpreter.SetVariable("tolerance", 2);
        interpreter.SetFunction("above", (FlowValue value, int minimum) => value.Json.Deserialize<int>() > minimum);
        interpreter.Reference(typeof(MyRules));
    }));
```

The C# adapter exposes the native interpreter configuration callback and parser
options in its constructor. Configure once before publishing the instance; do not
retain/mutate the interpreter afterward. Exposed objects and functions must be
safe for concurrent calls. Assignment is disabled initially; host configuration
can explicitly change native settings.

The existing JSON expression adapter also accepts a native environment factory:

```csharp
var engine = new JsonataFlowExpressionEngine(() =>
{
    var environment = new EvaluationEnvironment { MaxDepth = 100 };
    environment.BindValue("tolerance", JToken.Parse("2"));
    environment.BindFunction("allowed", (Func<int, bool>)(value => value > 0));
    return environment;
});
```

Return a fresh environment per evaluation, never a shared mutable one. Native
functions must be thread-safe. Per-call variables override environment bindings
of the same name. They are available as both root fields (`input`, `minimum`) and
native variables (`$input`, `$minimum`).

## Selection and data

- A source workflow's `ExpressionEngine` overrides the application default.
  This rule is identical for output and input declarations, including links
  across workflows. System-output conditions use the application default.
- Without either name, the existing unkeyed `IFlowExpressionEngine` registration
  is used. An explicit unknown name produces `MissingConditionEngine`; it never
  silently falls back. An unused selection does not require an engine to exist.
- `ApplicationDefinitionBuilder.UseExpressions("standard")` and
  `WorkflowDefinitionBuilder.UseExpressions("rules")` expose the same selection
  in C#. `when:` does not resolve or invoke an expression engine.
- `Parameters` is an optional JSON object on a conditional link. Its keys must
  be identifiers; `input`, `payload`, and `message` are reserved. Duplicate keys,
  non-object values, and parameters without a condition are rejected.
- Parameter values are immutable per-link data, copied into an invocation-local
  context. They cannot change another link's parameters or the caller's context.
  Integers materialize as `int`/`long`, then numeric values as `decimal`/`double`;
  nested objects/arrays remain `JsonElement` for typed engines.
- `input` and `payload` both contain `message.Value`. On canonical runtime ports
  this is `FlowValue`, or null for an error envelope. The exact error remains on
  `message.Error`; it is never substituted into a payload variable with a
  different declared type. Direct document and hosted execution use this same
  convention. Read structured values through `ToJsonElement()` or a host helper;
  JSON fields do not automatically become C# properties.
- For C# conditions that handle both cases, use an error guard, for example
  `message.IsError || above(input, minimum)`. The helper is not invoked for the
  error case. A value-only expression that dereferences a null payload still
  fails visibly through the existing condition diagnostics.
- Native configuration and executable imports/functions are never deserialized
  from the document. These engines are not security sandboxes: hosts must control
  exposed capabilities and decide which expression authors they trust.

Compilation caches are scoped to one application compilation, separated by engine
selection, input type, expression, and parameters. Changing effective selection
or link parameters participates in normal revision planning. Invalid compilation
rejects the revision before it becomes active. Designer load/save preserves names,
parameters, and declaration side.

## Runnable example and compatibility

The canonical host no longer substitutes an error object into `input`/`payload`.
Migrate expressions that read error fields through those aliases to the envelope
error instead (`message.Error` in C#, `message.error` in the JSON expression
adapter). This intentional breaking correction preserves typed argument binding
and makes short-circuit error guards work consistently.

Run `dotnet run --project samples/FluxFlow.CompositionSample -- --json` to load
[the saved sample](../samples/FluxFlow.CompositionSample/application.json).
It uses canonical component ports, a named workflow engine, host-provided functions, input-side condition
parameters, and verifies exactly `ALPHA`, `BETA`. Without `--json`, the same sample
demonstrates ordinary C# `when:` and expression links.

The host's `text(input)` function projects a canonical value to a string. The
saved expressions use `hasText(text(input))` and `text(input) != blocked`; native
function configuration stays in host code, while `blocked` remains ordinary
per-link JSON data. The default expression adapter still reads the JSON value
directly. No component-specific CLR model is required by the runtime.

`ExpressionEngine` is now reserved as a workflow metadata key and cannot also
be a component name. Existing documents without the new fields retain their
previous shape. Public constructor signatures changed and consumers must rebuild;
this development change has not updated existing package versions or published
artifacts. Include these changes and the port-method rename in the next deliberately
versioned release. No core package takes a dependency on either optional adapter.
