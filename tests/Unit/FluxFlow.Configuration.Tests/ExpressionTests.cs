using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Mapping;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class ExpressionTests
{
    [Theory]
    [InlineData("csharp", false)]
    [InlineData("csharp", true)]
    [InlineData("jsonata", false)]
    [InlineData("jsonata", true)]
    public void Engine_default_is_opt_in_and_shares_the_named_instance(string name, bool useAsDefault)
    {
        var services = new ServiceCollection();
        if (name == "csharp") services.AddCSharpExpressionEngine(useAsDefault);
        else services.AddJsonataExpressionEngine(useAsDefault);
        using var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredKeyedService<IFlowExpressionEngine>(name);
        engine.Name.ShouldBe(name);
        if (useAsDefault) provider.GetRequiredService<IFlowExpressionEngine>().ShouldBeSameAs(engine);
        else provider.GetService<IFlowExpressionEngine>().ShouldBeNull();
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("jsonata")]
    public async Task Router_predicate_receives_original_message_context(string engine)
    {
        using var provider = new ServiceCollection().AddCSharpExpressionEngine().AddJsonataExpressionEngine().BuildServiceProvider();
        var original = Message("{\"number\":42}");
        var message = original.WithValue(original.Value);
        var expression = engine == "csharp"
            ? $"value.number == 42 && headers.factory.code == \"SE01\" && messageId == \"{message.MessageId.Value}\" && traceId == \"{message.TraceId.Value}\" && correlationId == \"{message.CorrelationId!.Value.Value}\" && causationId == \"{original.MessageId.Value}\" && timestamp.ToString(\"O\") == \"{message.Timestamp:O}\""
            : $"$value.number = 42 and $headers.factory.code = 'SE01' and $messageId = '{message.MessageId.Value}' and $traceId = '{message.TraceId.Value}' and $correlationId = '{message.CorrelationId!.Value.Value}' and $causationId = '{original.MessageId.Value}' and $timestamp = '{message.Timestamp:O}'";
        var evaluate = provider.GetRequiredKeyedService<IFlowExpressionEngine>(engine).Compile<bool>(expression);
        await using var router = new Router(new() { Routes = [input => evaluate(FlowMapContext.ForMessage(input))] });
        var matched = new BufferBlock<FlowMessage>();
        var unmatched = new BufferBlock<FlowMessage>();
        using var route = router.Outputs[0].LinkTo(matched, new() { PropagateCompletion = true });
        using var fallback = router.Unmatched.LinkTo(unmatched, new() { PropagateCompletion = true });
        var next = Message("{\"number\":42}");
        await router.Input.SendAsync(message);
        await router.Input.SendAsync(next);
        router.Complete();
        (await matched.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        (await unmatched.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(next);
        await router.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        matched.Count.ShouldBe(0);
        unmatched.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("csharp", "input.number > minimum && payload.number == value.number")]
    [InlineData("jsonata", "$input.number > $minimum and $payload.number = $value.number")]
    public void Shared_engine_preserves_aliases_and_bound_parameters(string engine, string expression)
    {
        using var provider = new ServiceCollection().AddCSharpExpressionEngine().AddJsonataExpressionEngine().BuildServiceProvider();
        var compiler = provider.GetRequiredKeyedService<IFlowExpressionEngine>(engine);
        var predicate = compiler.Compile<bool>(expression, new Dictionary<string, Type>
        {
            ["input"] = typeof(FlowValue), ["payload"] = typeof(FlowValue), ["minimum"] = typeof(int)
        });
        var variables = new Dictionary<string, object?>(FlowMapContext.ForMessage(Message("{\"number\":42}")).Variables)
        { ["minimum"] = 40 };
        predicate(new FlowMapContext { Variables = variables }).ShouldBeTrue();
        variables["minimum"] = 50;
        predicate(new FlowMapContext { Variables = variables }).ShouldBeFalse();
    }

    [Theory]
    [InlineData("csharp", "42")]
    [InlineData("csharp", "\"true\"")]
    [InlineData("jsonata", "42")]
    [InlineData("jsonata", "'true'")]
    [InlineData("jsonata", "null")]
    public void Predicate_does_not_coerce_non_boolean_results(string engine, string expression)
    {
        using var provider = new ServiceCollection().AddCSharpExpressionEngine().AddJsonataExpressionEngine().BuildServiceProvider();
        var compiler = provider.GetRequiredKeyedService<IFlowExpressionEngine>(engine);
        Should.Throw<Exception>(() => compiler.Compile<bool>(expression)(FlowMapContext.ForMessage(Message("{}"))));
    }

    private static FlowMessage Message(string json, string site = "SE01")
        => FlowMessage.Create(new JsonFlowValue(JsonSerializer.Deserialize<JsonElement>(json)),
            correlationId: CorrelationId.New(), headers: new Dictionary<string, FlowValue>
            { ["factory"] = new JsonFlowValue(JsonSerializer.SerializeToElement(new { code = site })) });

    [Theory]
    [InlineData("value.customer.name", "\"Ali\"")]
    [InlineData("value.items[0].quantity + 1", "13")]
    [InlineData("value[\"factory-code\"]", "\"SE01\"")]
    [InlineData("value.nothing", "null")]
    [InlineData("value.text", "\"001\"")]
    [InlineData("value.flag", "true")]
    [InlineData("value.amount + 0.1", "12.35")]
    [InlineData("value.large", "9223372036854775807")]
    [InlineData("value.items", "[{\"quantity\":12}]")]
    [InlineData("headers", "{\"factory\":{\"code\":\"SE01\"}}")]
    [InlineData("new object[] { value.customer, headers.factory, (object)(messageId != null) }", "[{\"name\":\"Ali\"},{\"code\":\"SE01\"},true]")]
    public async Task Csharp_dot_access_and_output_keep_json_shape(string expression, string expected)
    {
        var message = Message("""{"customer":{"name":"Ali"},"items":[{"quantity":12}],"factory-code":"SE01","nothing":null,"text":"001","flag":true,"amount":12.25,"large":9223372036854775807}""");
        var result = await CSharpExpressions.Compile(expression)(message, default);
        result.Json.GetRawText().ShouldBe(expected);
    }

    [Fact]
    public async Task Csharp_has_separate_metadata_parameters_and_native_timestamp()
    {
        var message = Message("{}");
        var map = CSharpExpressions.Compile("new object[] { messageId, traceId, correlationId, causationId, (object)timestamp.Year, headers.factory.code }");
        var result = (await map(message, default)).Json;
        result[0].GetString().ShouldBe(message.MessageId.Value);
        result[1].GetString().ShouldBe(message.TraceId.Value);
        result[2].GetString().ShouldBe(message.CorrelationId!.Value.Value);
        result[3].ValueKind.ShouldBe(JsonValueKind.Null);
        result[4].GetInt32().ShouldBe(message.Timestamp.Year);
        result[5].GetString().ShouldBe("SE01");
    }

    [Theory]
    [InlineData("customer.name", "\"Ali\"")]
    [InlineData("$value.items[0].quantity + 1", "13")]
    [InlineData("[null, true, '001']", "[null,true,\"001\"]")]
    [InlineData("{'name': customer.name, 'site': $headers.factory.code}", "{\"name\":\"Ali\",\"site\":\"SE01\"}")]
    public async Task Jsonata_accepts_payload_and_separate_bindings(string expression, string expected)
    {
        var result = await JsonataExpressions.Compile(expression)(Message("""{"customer":{"name":"Ali"},"items":[{"quantity":12}]}"""), default);
        // Normalize library whitespace without changing the JSON value.
        JsonSerializer.Serialize(result.Json).ShouldBe(expected);
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("jsonata")]
    public async Task Compiled_function_is_reused_without_cross_message_state(string engine)
    {
        var compile = Compiler(engine);
        var map = compile(engine == "csharp" ? "new object[] { value.number, headers.factory.code, messageId }"
            : "[number, $headers.factory.code, $messageId]");
        await Parallel.ForEachAsync(Enumerable.Range(0, 200), async (index, cancellation) =>
        {
            var message = Message($"{{\"number\":{index}}}", index.ToString());
            var json = (await map(message, cancellation)).Json;
            json[0].GetInt32().ShouldBe(index);
            json[1].GetString().ShouldBe(index.ToString());
            json[2].GetString().ShouldBe(message.MessageId.Value);
        });
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await map(Message("{}"), canceled.Token));
    }

    [Theory]
    [InlineData("csharp", "value.missing")]
    [InlineData("jsonata", "missing")]
    public async Task Missing_result_is_error_not_silent_null(string engine, string expression)
        => await Should.ThrowAsync<Exception>(async () => await Compiler(engine)(expression)(Message("{}"), default));

    [Fact]
    public async Task Csharp_does_not_round_decimal_and_does_not_mutate_input()
    {
        var message = Message("{\"precise\":0.1234567890123456789012345678}");
        (await CSharpExpressions.Compile("value.precise")(message, default)).Json.GetDecimal()
            .ShouldBe(0.1234567890123456789012345678m);
        await Should.ThrowAsync<Exception>(async () => await CSharpExpressions.Compile("value.precise = 2")(message, default));
        message.Value.Json.GetProperty("precise").GetDecimal().ShouldBe(0.1234567890123456789012345678m);
    }

    private static Func<string, Func<FlowMessage, CancellationToken, ValueTask<FlowValue>>> Compiler(string engine)
        => engine == "csharp" ? CSharpExpressions.Compile : JsonataExpressions.Compile;
}
