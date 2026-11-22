using System.Diagnostics;
using System.Dynamic;
using System.Text.Json;
using DynamicExpresso;
using FluxFlow.Core.Messaging;

// Isolated feasibility experiment, not a production expression API.
var interpreter = new Interpreter(InterpreterOptions.Default | InterpreterOptions.LateBindObject);
Func<string, Func<object, object, string, bool>> compile = expression =>
    interpreter.ParseAsDelegate<Func<object, object, string, bool>>(
        expression, "value", "headers", "messageId");
var predicate = compile("value.customer.name == \"Ali\" && value.items[0].quantity > 10 && headers.factory.code == \"SE01\" && messageId == \"m1\"");
var headers = new Dictionary<string, FlowValue>
{
    ["factory"] = new JsonFlowValue(JsonSerializer.SerializeToElement(new { code = "SE01" }))
};
object AdaptHeaders(bool eager)
{
    // Headers already contain FlowValues: reuse each value's JSON representation.
    IDictionary<string, object?> result = new ExpandoObject();
    foreach (var (key, value) in headers) result[key] = JsonView.Read(value.Json, eager);
    return result;
}
foreach (var eager in new[] { false, true })
{
    using var document = JsonDocument.Parse("""
        {"customer":{"name":"Ali"},"items":[{"quantity":12}],"text":"001",
         "nothing":null,"flag":true,"factory-code":"SE01","decimal":12.25}
        """);
    var flow = new JsonFlowValue(document.RootElement);
    document.Dispose(); // FlowValue owns its snapshot; adapters must remain valid.
    var value = JsonView.Read(flow.Json, eager)!;
    var h = AdaptHeaders(eager);
    Check(predicate(value, h, "m1"), "nested properties, arrays, headers and string IDs");
    Check(compile("value.text == \"001\" && value.nothing == null && value.flag == true && value.decimal == 12.25m")(value, h, "m1"), "scalar preservation");
    // ExpandoObject itself does not provide a string indexer to this engine.
    if (!eager) Check(compile("value[\"factory-code\"] == \"SE01\"")(value, h, "m1"), "non-identifier key");
    try
    {
        compile("value.missing == null")(value, h, "m1");
        throw new InvalidOperationException("Missing property silently accepted.");
    }
    catch (Exception error) when (error is not InvalidOperationException)
    {
        Console.WriteLine($"Missing field ({(eager ? "eager" : "view")}): {error.GetType().Name}");
    }
    Parallel.For(0, 500, i =>
        Check(predicate(JsonView.Read(flow.Json, eager)!, AdaptHeaders(eager), "m1"), "concurrent evaluation"));
}
var counted = new SampleValue(4);
Parallel.For(0, 500, i => JsonView.Read(counted.Json, false));
Check(counted.JsonCreations == 1, "FlowValue creates JSON once across readers");
Console.WriteLine($"Checks passed; runtime={Environment.Version}; CPU={Environment.ProcessorCount}; engine=2.19.3");
Console.WriteLine("Microseconds/op and allocated bytes/op; median of 5 rounds, 10000 operations/round. Includes adapters and evaluation, excludes compilation.");
foreach (var width in new[] { 4, 100 })
{
    var cached = new SampleValue(width);
    _ = cached.Json;
    foreach (var cold in new[] { false, true })
    foreach (var mode in new[] { "direct JSON", "JSON view", "eager Expando" })
    {
        bool Execute()
        {
            var flow = cold ? new SampleValue(width) : cached;
            if (mode == "direct JSON")
            {
                var json = flow.Json;
                return json.GetProperty("customer").GetProperty("name").GetString() == "Ali"
                    && json.GetProperty("items")[0].GetProperty("quantity").GetInt64() > 10
                    && headers["factory"].Json.GetProperty("code").GetString() == "SE01";
            }
            var eager = mode == "eager Expando";
            return predicate(JsonView.Read(flow.Json, eager)!, AdaptHeaders(eager), "m1");
        }
        for (var i = 0; i < 3000; i++) Check(Execute(), "warmup");
        var rounds = new List<(double Us, long Bytes)>();
        for (var round = 0; round < 5; round++)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < 10000; i++) Check(Execute(), "measurement");
            rounds.Add((Stopwatch.GetElapsedTime(start).TotalMicroseconds / 10000,
                (GC.GetAllocatedBytesForCurrentThread() - bytes) / 10000));
        }
        var median = rounds.OrderBy(r => r.Us).ElementAt(2);
        Console.WriteLine($"fields={width}, {(cold ? "fresh typed FlowValue" : "cached JSON")}, {mode}: {median.Us:F3} us, {median.Bytes} B");
    }
}

static void Check(bool result, string name)
{
    if (!result) throw new InvalidOperationException($"Check failed: {name}");
}

public sealed class SampleValue(int width) : FlowValue
{
    public int JsonCreations;
    protected override JsonElement CreateJson()
    {
        Interlocked.Increment(ref JsonCreations);
        return JsonSerializer.SerializeToElement(new
        {
            customer = new { name = "Ali" },
            items = new[] { new { quantity = 12 } },
            unused = Enumerable.Range(0, width).ToDictionary(i => $"field{i}", i => new { text = "sample", number = i })
        });
    }
}

// Read-only lazy view: neither reparses JSON text nor copies the whole document.
// Numeric policy here is experimental (Int64, then Decimal, then Double).
public sealed class JsonView(JsonElement element) : DynamicObject
{
    public static object? Read(JsonElement value, bool eager) => value.ValueKind switch
    {
        JsonValueKind.Object => eager ? Expand(value) : new JsonView(value),
        JsonValueKind.Array => eager ? value.EnumerateArray().Select(v => Read(v, true)).ToArray() : new JsonView(value),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => Number(value),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new ArgumentException("Undefined JSON is not an expression value.")
    };

    private static object Number(JsonElement value)
    {
        if (value.TryGetInt64(out var integer)) return integer;
        if (value.TryGetDecimal(out var number)) return number;
        return value.GetDouble();
    }

    private static object Expand(JsonElement value)
    {
        IDictionary<string, object?> result = new ExpandoObject();
        foreach (var property in value.EnumerateObject()) result[property.Name] = Read(property.Value, true);
        return result;
    }

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = Read(element.GetProperty(binder.Name), false);
        return true;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)
    {
        result = indexes.Length == 1 ? indexes[0] switch
        {
            string name => Read(element.GetProperty(name), false),
            int index => Read(element[index], false),
            _ => throw new ArgumentException("Expected a property name or array index.")
        } : throw new ArgumentException("Expected one index.");
        return true;
    }
}
