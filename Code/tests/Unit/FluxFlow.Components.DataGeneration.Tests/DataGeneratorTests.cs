using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.DataGeneration;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.DataGeneration.Tests;

public sealed class DataGeneratorTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static FlowMessage Message(object value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));

    [Fact]
    public async Task Combines_input_context_and_random_functions_and_preserves_lineage()
    {
        const string expression = """
        {
          'deviceId': deviceId, 'same': $input.deviceId = $payload.deviceId and $value.deviceId = deviceId,
          'site': $headers.site, 'id': $messageId, 'trace': $traceId, 'time': $timestamp,
          'integer': $fakeInt(1, 1000), 'decimal': $fakeDecimal(15, 35),
          'flag': $fakeBool(), 'text': $fakeString(12), 'pattern': $fakePattern('TEST-####'),
          'name': $fakeFullName(), 'email': $fakeEmail(), 'uuid': $fakeUuid(),
          'items': [$fakeInt(7, 7), $fakeDecimal(2.5, 2.5)]
        }
        """;
        await using var generator = new DataGenerator(new() { Expression = expression, Locale = "sv" });
        var capture = new BufferBlock<FlowMessage>();
        using var link = generator.Output.LinkTo(capture);
        var original = Message(new { deviceId = "sensor-01" }).WithHeaders(new Dictionary<string, FlowValue> { ["site"] = "SE01" });
        await generator.Input.SendAsync(original);
        var result = await capture.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse(result.Error?.Message);
        var value = result.Value.Json;
        value.GetProperty("deviceId").GetString().ShouldBe("sensor-01");
        value.GetProperty("same").GetBoolean().ShouldBeTrue();
        value.GetProperty("site").GetString().ShouldBe("SE01");
        value.GetProperty("id").GetString().ShouldBe(original.MessageId.Value);
        value.GetProperty("trace").GetString().ShouldBe(original.TraceId.Value);
        DateTimeOffset.Parse(value.GetProperty("time").GetString()!).ShouldBe(original.Timestamp);
        value.GetProperty("integer").GetInt32().ShouldBeInRange(1, 1000);
        value.GetProperty("decimal").GetDecimal().ShouldBeInRange(15m, 35m);
        value.GetProperty("flag").ValueKind.ShouldBeOneOf(JsonValueKind.True, JsonValueKind.False);
        value.GetProperty("text").GetString()!.Length.ShouldBe(12);
        value.GetProperty("pattern").GetString()!.ShouldMatch("^TEST-[0-9]{4}$");
        value.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        value.GetProperty("email").GetString()!.ShouldContain("@");
        Guid.TryParse(value.GetProperty("uuid").GetString(), out _).ShouldBeTrue();
        value.GetProperty("items")[0].GetInt32().ShouldBe(7);
        value.GetProperty("items")[1].GetDecimal().ShouldBe(2.5m);
        result.MessageId.ShouldNotBe(original.MessageId);
        result.TraceId.ShouldBe(original.TraceId);
        result.CausationId!.Value.Value.ShouldBe(original.MessageId.Value);
        result.Headers["site"].Json.GetString().ShouldBe("SE01");
        original.Value.Json.TryGetProperty("integer", out _).ShouldBeFalse();
        generator.Complete();
        await generator.Completion.WaitAsync(Deadline);
        capture.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("$fakeInt(10, 1)")]
    [InlineData("$fakeDecimal(10, 1)")]
    [InlineData("$fakeString(-1)")]
    [InlineData("$fakeInt('invalid', 10)")]
    [InlineData("$fakeDate('2026-02-30', '2026-03-01')")]
    [InlineData("$fakeDate('2026-03-01', '2026-02-01')")]
    [InlineData("$fakeDate('10/04/2026', '10/05/2026')")]
    [InlineData("$fakeDateTime('2026-01-01T10:00:00', '2026-01-02T10:00:00Z')")]
    [InlineData("$fakeDateTime('2026-01-01T10:00:00Z', '2026-01-01T11:00:00+02:00')")]
    [InlineData("$fakeTime('24:00', '25:00')")]
    [InlineData("$fakeTime('23:00', '01:00')")]
    [InlineData("$fakeTimeSpan('-00:00:01')")]
    [InlineData("$fakeTimeSpan('one hour')")]
    [InlineData("$fakePast(0)")]
    [InlineData("$fakeFuture(-1)")]
    [InlineData("$fakeRecent(0)")]
    [InlineData("$fakeSoon(-1)")]
    [InlineData("$unknownFunction()")]
    [InlineData("missingField")]
    public async Task Runtime_failure_produces_error_and_next_message_recovers(string failingExpression)
    {
        await using var generator = new DataGenerator(new() { Expression = $"bad ? ({failingExpression}) : $fakeInt(9, 9)" });
        var capture = new BufferBlock<FlowMessage>();
        using var link = generator.Output.LinkTo(capture);
        var bad = Message(new { bad = true });
        await generator.Input.SendAsync(bad);
        var error = await capture.ReceiveAsync(Deadline);
        error.IsError.ShouldBeTrue();
        error.TraceId.ShouldBe(bad.TraceId);
        await generator.Input.SendAsync(Message(new { bad = false }));
        (await capture.ReceiveAsync(Deadline)).Value.Json.GetInt32().ShouldBe(9);
    }

    [Fact]
    public async Task Upstream_errors_bypass_expression_and_arrays_and_null_are_single_payloads()
    {
        await using var generator = new DataGenerator(new() { Expression = "array ? [1, 2, 3] : null" });
        var capture = new BufferBlock<FlowMessage>();
        using var link = generator.Output.LinkTo(capture);
        var upstream = FlowMessage.Create(new FlowError("upstream", "failed", "test"));
        await generator.Input.SendAsync(upstream);
        (await capture.ReceiveAsync(Deadline)).ShouldBeSameAs(upstream);
        await generator.Input.SendAsync(Message(new { array = true }));
        (await capture.ReceiveAsync(Deadline)).Value.Json.GetArrayLength().ShouldBe(3);
        await generator.Input.SendAsync(Message(new { array = false }));
        (await capture.ReceiveAsync(Deadline)).Value.Json.ValueKind.ShouldBe(JsonValueKind.Null);
        generator.Complete();
        await generator.Completion.WaitAsync(Deadline);
        capture.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("", "en")]
    [InlineData("(", "en")]
    [InlineData("$", "unknown-locale")]
    public void Invalid_expression_or_locale_fails_construction(string expression, string locale)
        => Should.Throw<Exception>(() => new DataGenerator(new() { Expression = expression, Locale = locale }));

    [Fact]
    public async Task Cancellation_closes_input_and_sequential_execution_is_explicit()
    {
        Should.Throw<ArgumentException>(() => new DataGenerator(new()
        {
            Expression = "$", Execution = new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 2 }
        }));
        using var cancellation = new CancellationTokenSource();
        await using var generator = new DataGenerator(new()
        {
            Expression = "$fakeInt(1, 2)", Execution = new ExecutionDataflowBlockOptions { CancellationToken = cancellation.Token }
        });
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => generator.Completion.WaitAsync(Deadline));
        (await generator.Input.SendAsync(Message(1))).ShouldBeFalse();
    }

    [Fact]
    public async Task Configuration_reads_file_at_build_snapshots_settings_and_deduplicates_links()
    {
        var file = Path.Combine(Path.GetTempPath(), "fluxflow-generator-" + Guid.NewGuid().ToString("N") + ".jsonata");
        try
        {
            using var config = Configuration(file);
            config["Workflows:demo:generate:Output"] = "next.Input";
            config["Workflows:demo:next:Type"] = "dataGenerator";
            config["Workflows:demo:next:Expression"] = "$";
            config["Workflows:demo:next:Input"] = "generate.Output";
            var services = new ServiceCollection().AddDataGenerator();
            services.AddFluxFlowApplication(config);
            services.Count(d => d.ServiceType == typeof(LinkFactory) && !d.IsKeyedService).ShouldBe(1);
            config["Workflows:demo:generate:Expression"] = "(";
            File.WriteAllText(file, "{'deviceId': deviceId, 'count': $fakeInt(3, 3)}");
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            File.WriteAllText(file, "(");
            (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
            var capture = new BufferBlock<FlowMessage>();
            using var link = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.next.Output").LinkTo(capture);
            provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.generate.Events").ShouldNotBeNull();
            await app.StartAsync();
            var input = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.generate.Input");
            for (var index = 0; index < 3; index++)
            {
                await input.SendAsync(Message(new { deviceId = index }));
                var result = await capture.ReceiveAsync(Deadline);
                result.IsError.ShouldBeFalse(result.Error?.Message);
                result.Value.Json.GetProperty("count").GetInt32().ShouldBe(3);
                result.Value.Json.GetProperty("deviceId").GetInt32().ShouldBe(index);
            }
            await app.StopAsync().WaitAsync(Deadline);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("Expression", null)]
    [InlineData("OutputCapacity", "0")]
    [InlineData("Locale", "")]
    public void Invalid_configuration_leaves_services_unchanged(string property, string? value)
    {
        using var config = Configuration("$");
        config["Workflows:demo:generate:" + property] = value;
        var services = new ServiceCollection().AddDataGenerator();
        var before = services.ToArray();
        Should.Throw<Exception>(() => services.AddFluxFlowApplication(config));
        services.ToArray().ShouldBe(before);
    }

    [Theory]
    [InlineData("(")]
    [InlineData("./missing-generator.jsonata")]
    [InlineData("./wrong-language.cs")]
    public async Task Build_failures_identify_the_component(string expression)
    {
        using var config = Configuration(expression);
        var services = new ServiceCollection().AddDataGenerator();
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<Exception>(() => provider.BuildApplicationAsync());
        error.ToString().ShouldContain("demo.generate");
    }

    private static ConfigurationRoot Configuration(string expression) => (ConfigurationRoot)new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:demo:generate:Type"] = "dataGenerator",
            ["Workflows:demo:generate:Expression"] = expression
        }).Build();
}
