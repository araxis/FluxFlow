using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mapping;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Configuration.Mapping;
using FluxFlow.Core.Application;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class MapperConfigurationTests
{
    [Fact]
    public async Task Mapper_requires_its_engine_even_when_a_link_default_is_registered()
    {
        var configuration = Config("", "number * 2");
        using var configLifetime = (IDisposable)configuration;
        var services = AddSink(new ServiceCollection().AddMapper().AddJsonataExpressionEngine(useAsDefault: true));
        await Should.ThrowAsync<InvalidOperationException>(() => services.AddFluxFlowApplicationAsync(configuration).AsTask());
    }

    [Theory]
    [InlineData("csharp", "value > minimum && headers.site == \"SE01\" && messageId != null")]
    [InlineData("jsonata", "$value > $minimum and $headers.site = 'SE01' and $exists($messageId)")]
    public async Task Engine_registration_supports_mapper_and_conditional_links(string engine, string condition)
    {
        var configuration = Config(engine, engine == "csharp" ? "value.number * 2" : "number * 2");
        using var configLifetime = (IDisposable)configuration;
        configuration["workflows:main:map:output"] = null;
        configuration["workflows:main:map:output:port"] = "sink.Input";
        configuration["workflows:main:map:output:condition"] = condition;
        configuration["workflows:main:map:output:engine"] = engine;
        configuration["workflows:main:map:output:parameters:minimum"] = "40";
        var services = AddSink(new ServiceCollection().AddMapper().AddCSharpExpressionEngine().AddJsonataExpressionEngine());
        await services.AddFluxFlowApplicationAsync(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var mapper = (Mapper)app.GetComponent("main.map");
        var sink = ((Sink)app.GetComponent("main.sink")).Received;
        await mapper.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { number = 21 })),
            headers: new Dictionary<string, FlowValue> { ["site"] = "SE01" }));
        (await sink.ReceiveAsync(TimeSpan.FromSeconds(10))).Value.Json.GetInt32().ShouldBe(42);
    }

    [Theory]
    [InlineData("csharp", "value.number * 2")]
    [InlineData("JSONATA", "number * 2")]
    [InlineData("jsonata", "base64:bnVtYmVyICogMg==")]
    [InlineData("csharp", "base64:dmFsdWUubnVtYmVyICogMg==")]
    public async Task Configuration_builds_engine_neutral_mapper_and_keeps_lineage(string engine, string expression)
    {
        var configuration = Config(engine, expression);
        using var configLifetime = (IDisposable)configuration;
        var services = AddSink(new ServiceCollection().AddMapper().AddCSharpExpressionEngine().AddJsonataExpressionEngine());
        await services.AddFluxFlowApplicationAsync(configuration);
        configuration["workflows:main:map:expression"] = "invalid after registration";
        for (var build = 0; build < 2; build++)
        {
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            var mapper = (Mapper)app.GetComponent("main.map");
            var sink = ((Sink)app.GetComponent("main.sink")).Received;
            await app.StartAsync();
            var message = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { number = 21 })),
                correlationId: CorrelationId.New(), headers: new Dictionary<string, FlowValue> { ["site"] = "SE01" });
            await mapper.Input.SendAsync(message);
            var output = await sink.ReceiveAsync(TimeSpan.FromSeconds(10));
            output.Value.Json.GetInt32().ShouldBe(42);
            output.MessageId.ShouldNotBe(message.MessageId);
            output.CausationId.ShouldBe(message.MessageId);
            output.TraceId.ShouldBe(message.TraceId);
            output.CorrelationId.ShouldBe(message.CorrelationId);
            output.Headers["site"].ShouldBeSameAs(message.Headers["site"]);
        }
    }

    [Theory]
    [InlineData("csharp", "value.number * 2")]
    [InlineData("jsonata", "$assert($exists(number), 'number missing'); number * 2")]
    public async Task Bad_message_becomes_error_and_next_message_still_maps(string engine, string expression)
    {
        // Parentheses make JSONata's multi-statement expression explicit.
        if (engine == "jsonata") expression = "(" + expression + ")";
        var configuration = Config(engine, expression);
        using var configLifetime = (IDisposable)configuration;
        var services = AddSink(new ServiceCollection().AddMapper().AddCSharpExpressionEngine().AddJsonataExpressionEngine());
        await services.AddFluxFlowApplicationAsync(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var mapper = (Mapper)app.GetComponent("main.map");
        var sink = ((Sink)app.GetComponent("main.sink")).Received;
        await app.StartAsync();
        await mapper.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { }))));
        await mapper.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { number = 21 }))));
        (await sink.ReceiveAsync(TimeSpan.FromSeconds(10))).Error!.Code.ShouldBe("node.processing_failed");
        (await sink.ReceiveAsync(TimeSpan.FromSeconds(10))).Value.Json.GetInt32().ShouldBe(42);
    }

    [Theory]
    [InlineData("csharp", "value.")]
    [InlineData("jsonata", "{invalid")]
    [InlineData("unknown", "value")]
    [InlineData("csharp", "file:missing.csx")]
    public async Task Invalid_expression_fails_build_and_provider_owns_previous_component(string engine, string expression)
    {
        var configuration = Config(engine, expression);
        configuration["workflows:main:a:type"] = "probe";
        using var configLifetime = (IDisposable)configuration;
        Probe? created = null;
        var services = AddSink(new ServiceCollection().AddMapper().AddCSharpExpressionEngine().AddJsonataExpressionEngine());
        services.AddKeyedSingleton<ComponentRegistration>("probe", (registrations, section) =>
            registrations.AddComponentFactory(section.GetComponentAddress(), _ => created = new Probe()));
        await services.AddFluxFlowApplicationAsync(configuration);
        created.ShouldBeNull();
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<Exception>(async () => await provider.BuildApplicationAsync());
        error.ToString().ShouldContain("main.map");
        created.ShouldNotBeNull();
        created.Completion.IsCompleted.ShouldBeTrue();
        created.Disposed.ShouldBeFalse();
        await provider.DisposeAsync();
        created.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Expression_is_compiled_once_per_build_and_files_resolve_at_build()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxflow-expression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "map.csx");
        try
        {
            var configuration = Config("counted", "map.csx");
            using var configLifetime = (IDisposable)configuration;
            var compiles = 0;
            var services = AddSink(new ServiceCollection().AddMapper(directory));
            services.AddCSharpExpressionEngine();
            services.AddKeyedSingleton<IFlowExpressionEngine>("counted", (provider, _) =>
                new CountedEngine(provider.GetRequiredKeyedService<IFlowExpressionEngine>("csharp"), () => compiles++));
            await services.AddFluxFlowApplicationAsync(configuration);
            compiles.ShouldBe(0);
            await File.WriteAllTextAsync(file, "value + 1");
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            compiles.ShouldBe(1);
            await File.WriteAllTextAsync(file, "invalid");
            var mapper = (Mapper)app.GetComponent("main.map");
            var sink = ((Sink)app.GetComponent("main.sink")).Received;
            await app.StartAsync();
            for (var i = 0; i < 3; i++)
            {
                await mapper.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(i))));
                (await sink.ReceiveAsync(TimeSpan.FromSeconds(10))).Value.Json.GetInt32().ShouldBe(i + 1);
            }
            compiles.ShouldBe(1);
            new ExpressionSource("inline:value.cs").Read(directory).ShouldBe("value.cs");
            await File.WriteAllTextAsync(file, " ");
            Should.Throw<ArgumentException>(() => new ExpressionSource("file:map.csx").Read(directory));
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }

    private static IConfigurationRoot Config(string engine, string expression)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["workflows:main:map:type"] = "mapper",
            ["workflows:main:map:engine"] = engine,
            ["workflows:main:map:expression"] = expression,
            ["workflows:main:map:output"] = "sink.Input",
            ["workflows:main:sink:type"] = "sink"
        }).Build();

    private static IServiceCollection AddSink(IServiceCollection services)
        => services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) =>
            registrations.AddComponentFactory(section.GetComponentAddress(), _ => new Sink())
                .AddInputPort<Sink>(section.GetComponentAddress(), "Input", node => node.Input));

    private sealed class Sink : FlowTransformNode
    {
        public BufferBlock<FlowMessage> Received { get; } = new();
        protected override bool HandlesErrors => true;
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            Received.Post(message);
            return Task.FromResult<IEnumerable<FlowMessage>>([]);
        }
    }

    private sealed class CountedEngine(IFlowExpressionEngine inner, Action onCompile) : IFlowExpressionEngine
    {
        public string Name => "counted";
        public object? Evaluate(string expression, FlowMapContext context, Type resultType) => inner.Evaluate(expression, context, resultType);
        public Func<FlowMapContext, T> Compile<T>(string expression)
        {
            onCompile();
            return inner.Compile<T>(expression);
        }
    }

    private sealed class Probe : FlowTransformNode
    {
        public bool Disposed;
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([]);
        protected override ValueTask OnDisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
