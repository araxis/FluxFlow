# Mapping

Namespace: `FluxFlow.Engine.Mapping`

FluxFlow.Engine provides a small, composable set of mapping abstractions.
All are optional — your nodes can transform data any way they like.
These become useful when you want **user-configurable expressions** (e.g. stored
in JSON configuration and evaluated at runtime).

---

## IFlowMapper\<TInput, TOutput\>

Synchronous, stateless transformation.

```csharp
public interface IFlowMapper<in TInput, out TOutput>
{
    TOutput Map(TInput input, FlowMapContext context);
}
```

### DelegateFlowMapper

Creates a mapper from a lambda:

```csharp
IFlowMapper<string, int> lengthMapper =
    new DelegateFlowMapper<string, int>((input, _) => input.Length);
```

### Custom mapper example

```csharp
public sealed class UpperCaseMapper : IFlowMapper<string, string>
{
    public string Map(string input, FlowMapContext context)
        => input.ToUpperInvariant();
}
```

---

## IFlowPredicate\<TInput\>

```csharp
public interface IFlowPredicate<in TInput>
{
    bool IsMatch(TInput input);
}
```

### DelegateFlowPredicate

```csharp
IFlowPredicate<string> notEmpty =
    new DelegateFlowPredicate<string>(s => !string.IsNullOrWhiteSpace(s));
```

---

## FlowMapContext

Carries named variables into expression engines.

```csharp
public sealed record FlowMapContext
{
    public IReadOnlyDictionary<string, object?> Variables { get; init; }
}
```

Build a context from your message payload:

```csharp
var ctx = new FlowMapContext
{
    Variables = new Dictionary<string, object?>
    {
        ["topic"]      = envelope.Topic,
        ["payload"]    = envelope.PayloadUtf8,
        ["qos"]        = (int)envelope.QualityOfService,
        ["retain"]     = envelope.Retain,
        ["receivedAt"] = envelope.ReceivedAt
    }
};
```

---

## IFlowExpressionEngine

Evaluates string expressions against a `FlowMapContext`.

```csharp
public interface IFlowExpressionEngine
{
    string Name { get; }

    object? Evaluate(string expression, FlowMapContext context, Type resultType);

    // Generic convenience overload (default implementation)
    T Evaluate<T>(string expression, FlowMapContext context)
        => (T)Evaluate(expression, context, typeof(T))!;
}
```

Two engines ship with the library.

---

## DynamicExpressoFlowExpressionEngine

**Name:** `"dynamic-expresso"`  
**Package:** `DynamicExpresso.Core` (already in `FluxFlow.Engine.csproj`)

Evaluates C#-style expressions using the
[DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso) interpreter.
Variables from `FlowMapContext.Variables` are bound by name.

```csharp
var engine = new DynamicExpressoFlowExpressionEngine();

var ctx = new FlowMapContext
{
    Variables = new Dictionary<string, object?>
    {
        ["topic"] = "sensors/temperature",
        ["qos"]   = 1
    }
};

// Returns true when topic starts with "sensors/" and qos >= 1
bool match = engine.Evaluate<bool>(
    "topic.StartsWith(\"sensors/\") && qos >= 1",
    ctx);

// Map a value
string upper = engine.Evaluate<string>("topic.ToUpperInvariant()", ctx);
```

**Type references:** If a variable value is a `Type` object, it is registered
as a type reference (not a variable) so expressions can reference static members.

### Using as a predicate

```csharp
IFlowPredicate<MqttEnvelope> expressionPredicate = new ExpressionPredicate(
    engine,
    "qos >= 1 && topic.StartsWith(\"sensors/\")");

public sealed class ExpressionPredicate(
    IFlowExpressionEngine engine,
    string expression)
    : IFlowPredicate<MqttEnvelope>
{
    public bool IsMatch(MqttEnvelope input)
    {
        var ctx = BuildContext(input);
        try { return engine.Evaluate<bool>(expression, ctx); }
        catch { return false; }
    }

    private static FlowMapContext BuildContext(MqttEnvelope e) =>
        new() { Variables = new Dictionary<string, object?>
        {
            ["topic"]   = e.Topic,
            ["payload"] = e.PayloadUtf8,
            ["qos"]     = (int)e.Qos
        }};
}
```

---

## JsonataFlowExpressionEngine

**Name:** `"jsonata"`  
**Package:** `Jsonata.Net.Native` (already in `FluxFlow.Engine.csproj`)

Evaluates [JSONata](https://jsonata.org/) query/transform expressions.
Use this when the input is JSON and you want to extract, reshape, or
compute values using JSONata syntax.

```csharp
var engine = new JsonataFlowExpressionEngine();

// Variables["input"] must be a JSON string or a JsonNode
var ctx = new FlowMapContext
{
    Variables = new Dictionary<string, object?>
    {
        ["input"] = """{ "temperature": 21.5, "unit": "C" }"""
    }
};

// Extract a field
double temp = engine.Evaluate<double>("$.temperature", ctx);

// Transform
string summary = engine.Evaluate<string>(
    "\"Temp: \" & $string($.temperature) & $.unit", ctx);
```

---

## IFlowMapContextFactory

Optional factory interface for creating `FlowMapContext` from a typed message.
Implement this when you want to standardize variable names across node types.

```csharp
public interface IFlowMapContextFactory<in TInput>
{
    FlowMapContext Create(TInput input);
}
```

Example:

```csharp
public sealed class MqttEnvelopeContextFactory : IFlowMapContextFactory<MqttEnvelope>
{
    public FlowMapContext Create(MqttEnvelope envelope) => new()
    {
        Variables = new Dictionary<string, object?>
        {
            ["topic"]      = envelope.Topic,
            ["payload"]    = envelope.PayloadUtf8,
            ["qos"]        = (int)envelope.QualityOfService,
            ["retain"]     = envelope.Retain,
            ["receivedAt"] = envelope.ReceivedAt
        }
    };
}
```

Then use it in your node:

```csharp
private static readonly IFlowMapContextFactory<MqttEnvelope> _ctxFactory =
    new MqttEnvelopeContextFactory();

private bool EvaluatePredicate(MqttEnvelope envelope)
{
    var ctx = _ctxFactory.Create(envelope);
    return _engine.Evaluate<bool>(_expression, ctx);
}
```

---

## Choosing an engine

| Scenario | Engine |
|----------|--------|
| C# filter on strongly-typed variables | `DynamicExpressoFlowExpressionEngine` |
| JSON extraction or reshaping | `JsonataFlowExpressionEngine` |
| Hard-coded transformation | Plain C# (no engine needed) |
| User-authored mapping rules stored in JSON | Either engine — store expression string in `configuration` |

---

## Performance note

Both built-in engines **do not cache compiled expressions**.
For high-throughput nodes that evaluate the same expression millions of times,
wrap the engine in a caching adapter:

```csharp
public sealed class CachingExpressionEngine(IFlowExpressionEngine inner) : IFlowExpressionEngine
{
    private readonly ConcurrentDictionary<(string Expr, Type Result), Func<FlowMapContext, object?>>
        _cache = new();

    public string Name => inner.Name;

    public object? Evaluate(string expression, FlowMapContext context, Type resultType)
    {
        var compiled = _cache.GetOrAdd(
            (expression, resultType),
            key => ctx => inner.Evaluate(key.Expr, ctx, key.Result));

        return compiled(context);
    }
}
```
