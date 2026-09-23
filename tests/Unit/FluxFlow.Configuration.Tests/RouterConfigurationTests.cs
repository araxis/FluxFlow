using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing;
using FluxFlow.Configuration.Routing;
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

public sealed class RouterConfigurationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("csharp", "value > 0 && headers.site == \"SE01\" && messageId != null && traceId != null && timestamp.Year > 2000", "value > 10")]
    [InlineData("jsonata", "$value > 0 and $headers.site = 'SE01' and $exists($messageId) and $exists($traceId) and $exists($timestamp)", "$value > 10")]
    public async Task Named_routes_preserve_message_context_fanout_and_fallback(string engine, string first, string second)
    {
        using var config = Config(engine, first, second);
        var services = AddSink(new ServiceCollection().AddRouter().AddCSharpExpressionEngine().AddJsonataExpressionEngine());
        await services.AddFluxFlowApplicationAsync(config);
        config["workflows:main:route:routes:Priority"] = "invalid after Add";
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var router = provider.GetRequiredKeyedService<IFlowNode>("main.route").ShouldBeOfType<Router>();
        var priority = provider.GetRequiredKeyedService<IFlowNode>("main.priority").ShouldBeOfType<Sink>();
        var large = provider.GetRequiredKeyedService<IFlowNode>("main.large").ShouldBeOfType<Sink>();
        var fallback = provider.GetRequiredKeyedService<IFlowNode>("main.fallback").ShouldBeOfType<Sink>();
        var errors = provider.GetRequiredKeyedService<IFlowNode>("main.errors").ShouldBeOfType<Sink>();
        var both = FlowMessage.Create(Number(20), headers: new Dictionary<string, FlowValue> { ["site"] = "SE01" });
        var neither = FlowMessage.Create(Number(-1), headers: both.Headers);
        await router.Input.SendAsync(both);
        await router.Input.SendAsync(neither);
        (await priority.Received.ReceiveAsync(Deadline)).ShouldBeSameAs(both);
        (await large.Received.ReceiveAsync(Deadline)).ShouldBeSameAs(both);
        (await fallback.Received.ReceiveAsync(Deadline)).ShouldBeSameAs(neither);
        await router.Input.SendAsync(FlowMessage.Create((FlowValue)"not a number"));
        (await errors.Received.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        fallback.Received.Count.ShouldBe(0);
        await router.Input.SendAsync(both);
        (await priority.Received.ReceiveAsync(Deadline)).ShouldBeSameAs(both);
        (await large.Received.ReceiveAsync(Deadline)).ShouldBeSameAs(both);
    }

    [Theory]
    [InlineData("Output")]
    [InlineData("Unmatched")]
    [InlineData("engine")]
    [InlineData("bad.name")]
    public async Task Invalid_route_names_leave_host_registration_unchanged(string name)
    {
        using var config = Config("csharp", "true", "true");
        config[$"workflows:main:route:routes:{name}"] = "true";
        var services = AddSink(new ServiceCollection().AddRouter());
        var original = services.ToArray();
        await Should.ThrowAsync<Exception>(async () => await services.AddFluxFlowApplicationAsync(config));
        services.ToArray().ShouldBe(original);
    }

    [Fact]
    public async Task Files_compile_once_at_build_and_repeated_build_reuses_component()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxflow-router-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "route.csx");
        try
        {
            using var config = Config("counted", "route.csx", "false");
            var compiles = 0;
            var services = AddSink(new ServiceCollection().AddRouter(directory).AddCSharpExpressionEngine());
            services.AddKeyedSingleton<IFlowExpressionEngine>("counted", (provider, _) =>
                new CountedEngine(provider.GetRequiredKeyedService<IFlowExpressionEngine>("csharp"), () => compiles++));
            await services.AddFluxFlowApplicationAsync(config);
            compiles.ShouldBe(0);
            await File.WriteAllTextAsync(file, "value > 0");
            await using var provider = services.BuildServiceProvider();
            await using var app = await provider.BuildApplicationAsync();
            compiles.ShouldBe(2);
            await File.WriteAllTextAsync(file, "invalid");
            (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
            await app.StartAsync();
            var router = provider.GetRequiredKeyedService<IFlowNode>("main.route").ShouldBeOfType<Router>();
            var sink = provider.GetRequiredKeyedService<IFlowNode>("main.priority").ShouldBeOfType<Sink>();
            var message = FlowMessage.Create(Number(1));
            await router.Input.SendAsync(message);
            (await sink.Received.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
            compiles.ShouldBe(2);
        }
        finally { File.Delete(file); Directory.Delete(directory); }
    }

    [Theory]
    [InlineData("csharp", "invalid (")]
    [InlineData("jsonata", "(")]
    public async Task Invalid_expression_fails_build_with_route_address(string engine, string expression)
    {
        using var config = Config(engine, expression, "true");
        var services = AddSink(new ServiceCollection().AddRouter().AddCSharpExpressionEngine().AddJsonataExpressionEngine());
        await services.AddFluxFlowApplicationAsync(config);
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<Exception>(() => provider.BuildApplicationAsync());
        error.ToString().ShouldContain("main.route");
        error.ToString().ShouldContain("Priority");
    }

    private static FlowValue Number(int value) => new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(value));

    private static ConfigurationRoot Config(string engine, string first, string second)
        => (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["workflows:main:route:type"] = "router",
            ["workflows:main:route:engine"] = engine,
            ["workflows:main:route:routes:Priority"] = first,
            ["workflows:main:route:routes:Large"] = second,
            ["workflows:main:route:Priority"] = "priority.Input",
            ["workflows:main:route:Unmatched"] = "fallback.Input",
            ["workflows:main:route:Output"] = "errors.Input",
            ["workflows:main:errors:type"] = "sink",
            ["workflows:main:priority:type"] = "sink",
            ["workflows:main:large:type"] = "sink",
            ["workflows:main:large:Input"] = "route.Large",
            ["workflows:main:fallback:type"] = "sink"
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
}
