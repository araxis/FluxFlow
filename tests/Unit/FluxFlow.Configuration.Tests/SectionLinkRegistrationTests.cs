using System.Text;
using System.Threading.Tasks.Dataflow;
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

public sealed class SectionLinkRegistrationTests
{
    [Theory]
    [InlineData("csharp", false)]
    [InlineData("csharp", true)]
    [InlineData("jsonata", false)]
    [InlineData("jsonata", true)]
    public async Task Named_link_engine_overrides_the_registered_default(string defaultEngine, bool explicitEngine)
    {
        var selected = explicitEngine ? (defaultEngine == "csharp" ? "jsonata" : "csharp") : defaultEngine;
        var expression = selected == "csharp" ? "value.number > 5" : "$value.number > 5";
        var engineField = explicitEngine ? ",\"Engine\":\"" + selected + "\"" : "";
        using var config = Read("{\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\",\"Output\":{\"Port\":\"sink.Input\",\"Condition\":\"" +
            expression + "\"" + engineField + "}},\"sink\":{\"Type\":\"sink\"}}}}");
        var services = Register(() => { });
        services.AddCSharpExpressionEngine(useAsDefault: defaultEngine == "csharp")
            .AddJsonataExpressionEngine(useAsDefault: defaultEngine == "jsonata");
        await services.AddFluxFlowApplicationAsync(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var source = provider.GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var sink = provider.GetRequiredKeyedService<IFlowNode>("main.sink").ShouldBeOfType<Sink>();
        var message = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(new { number = 6 })));
        await source.Input.SendAsync(message);
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        await app.StopAsync();
    }

    [Theory]
    [InlineData("host-block")]
    [InlineData("main.source.ExtraSetting")]
    public async Task Host_keyed_blocks_are_not_discovered_as_component_ports(string hostKey)
    {
        using var config = Read("""
        {"Workflows":{"main":{
          "source":{"Type":"source","ExtraSetting":"not a link","Output":"sink.Input"},
          "sink":{"Type":"sink"}
        }}}
        """);
        var services = Register(() => { });
        services.AddKeyedSingleton<ITargetBlock<FlowMessage>>(hostKey,
            (_, _) => throw new InvalidOperationException("Host input must not be resolved."));
        services.AddKeyedSingleton<ISourceBlock<FlowMessage>>(hostKey,
            (_, _) => throw new InvalidOperationException("Host output must not be resolved."));

        await services.AddFluxFlowApplicationAsync(config);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && item.IsKeyedService).ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var source = provider.GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var sink = provider.GetRequiredKeyedService<IFlowNode>("main.sink").ShouldBeOfType<Sink>();
        var message = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(1)));
        await source.Input.SendAsync(message);
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        await app.StopAsync();
    }

    [Fact]
    public async Task Section_links_are_registered_without_construction_and_keep_local_engine_and_snapshot()
    {
        using var config = Read("""
        {"Workflows":{
          "main":{"source":{"Type":"source","ExtraSetting":"ignored",
            "Output":[{"Port":"other.a.Input","Condition":"positive","Engine":"rules"},
                      {"Port":"other.b.Input","Condition":"positive","Engine":"rules"},
                      {"Port":"other.fallback.Input","Fallback":true}]}},
          "other":{"a":{"Type":"sink",
            "Input":{"Port":"main.source.Output","Condition":"positive","Engine":"rules"}},
            "b":{"Type":"sink"},"fallback":{"Type":"sink"}}
        }}
        """);
        var constructed = 0;
        var engine = new Engine();
        var services = Register(() => constructed++);
        services.AddKeyedSingleton<IFlowExpressionEngine>("rules", engine);
        await services.AddFluxFlowApplicationAsync(config);
        constructed.ShouldBe(0);
        engine.Compilations.ShouldBe(0);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(3);
        var keyedLinks = services.Where(item => item.ServiceType == typeof(LinkFactory) && item.IsKeyedService).ToArray();
        keyedLinks.Length.ShouldBe(3);
        config["workflows:main:source:Output:0:Condition"] = "changed after registration";
        config["workflows:main:source:Output:0:Engine"] = "changed after registration";
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        constructed.ShouldBe(4);
        var executingLinks = provider.GetServices<LinkFactory>().ToArray();
        executingLinks.Length.ShouldBe(3);
        foreach (var registration in keyedLinks)
        {
            var canonical = provider.GetRequiredKeyedService<LinkFactory>(registration.ServiceKey);
            canonical.ShouldBeSameAs(registration.KeyedImplementationInstance);
            executingLinks.ShouldContain(link => ReferenceEquals(link, canonical));
        }
        engine.Compilations.ShouldBe(1);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
        engine.Compilations.ShouldBe(1);
        await app.StartAsync();
        var source = provider.GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var positive = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(1)));
        var negative = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(-1)));
        await source.Input.SendAsync(positive);
        await source.Input.SendAsync(negative);
        foreach (var name in new[] { "a", "b", "fallback" })
        {
            var sink = provider.GetRequiredKeyedService<IFlowNode>("other." + name).ShouldBeOfType<Sink>();
            (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10)))
                .ShouldBeSameAs(name == "fallback" ? negative : positive);
        }
        await app.StopAsync();
        foreach (var name in new[] { "a", "b", "fallback" })
            provider.GetRequiredKeyedService<IFlowNode>("other." + name).ShouldBeOfType<Sink>().Received.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "rules")]
    [InlineData(true, "rules")]
    public async Task Both_link_directions_capture_parameters_and_compile_per_parameter_set(bool inputSide, string? engineKey)
    {
        var engineField = engineKey is null ? "" : ",\"Engine\":\"rules\"";
        string Link(string port, int minimum) => "{\"Port\":\"" + port + "\",\"Condition\":\"positive\",\"Parameters\":{\"minimum\":" + minimum + "}" + engineField + "}";
        var sourceLinks = inputSide ? "" : ",\"Output\":[" + Link("a.Input", 5) + "," + Link("b.Input", 6) + "]";
        var inputA = inputSide ? ",\"Input\":" + Link("source.Output", 5) : "";
        var inputB = inputSide ? ",\"Input\":" + Link("source.Output", 6) : "";
        using var config = Read("{\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\"" + sourceLinks +
            "},\"a\":{\"Type\":\"sink\"" + inputA + "},\"b\":{\"Type\":\"sink\"" + inputB + "}}}}");
        var services = Register(() => { });
        var engine = new Engine();
        if (engineKey is null) services.AddSingleton<IFlowExpressionEngine>(engine);
        else services.AddKeyedSingleton<IFlowExpressionEngine>(engineKey, engine);
        await services.AddFluxFlowApplicationAsync(config);
        config[inputSide ? "Workflows:main:a:Input:Parameters:minimum" : "Workflows:main:source:Output:0:Parameters:minimum"] = "100";
        engine.Compilations.ShouldBe(0);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        engine.Compilations.ShouldBe(2);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
        engine.Compilations.ShouldBe(2);
        await app.StartAsync();
        var source = provider.GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var message = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(6)));
        await source.Input.SendAsync(message);
        var a = provider.GetRequiredKeyedService<IFlowNode>("main.a").ShouldBeOfType<Sink>();
        (await a.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        await app.StopAsync();
        provider.GetRequiredKeyedService<IFlowNode>("main.b").ShouldBeOfType<Sink>().Received.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_explicit_engine_does_not_fall_back_to_default()
    {
        using var config = Read("""
        {"Workflows":{"main":{"source":{"Type":"source","Output":{"Port":"sink.Input","Condition":"positive","Engine":"missing"}},"sink":{"Type":"sink"}}}}
        """);
        var engine = new Engine();
        var services = Register(() => { });
        services.AddSingleton<IFlowExpressionEngine>(engine);
        await services.AddFluxFlowApplicationAsync(config);
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
        error.Message.ShouldContain("missing");
        engine.Compilations.ShouldBe(0);
    }

    [Theory]
    [InlineData("123", null)]
    [InlineData("[[\"sink.Input\"]]", null)]
    [InlineData("{\"Port\":[\"sink.Input\"]}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Condition\":{\"value\":\"positive\"}}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Condition\":\"positive\",\"Parameters\":[1,2]}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Fallback\":true,\"Condition\":\"positive\"}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Parameters\":{}}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Condition\":\"positive\",\"Parameters\":{\"input\":1}}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Delivery\":\"0\"}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Delivery\":\"Reliable, RejectWhenFull\"}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Unknown\":true}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Engine\":\"rules\"}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Condition\":\"positive\",\"Engine\":\" \"}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Delivery\":\"unknown\"}", null)]
    [InlineData("{\"Port\":\"sink.Input\",\"Delivery\":\"Reliable\"}",
        "{\"Port\":\"source.Output\",\"Delivery\":\"RejectWhenFull\"}")]
    public async Task Invalid_links_reject_registration_atomically(string output, string? input)
    {
        using var config = Read("{\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\",\"Output\":" + output +
            "},\"sink\":{\"Type\":\"sink\"" + (input is null ? "" : ",\"Input\":" + input) + "}}}}");
        var constructed = 0;
        var services = Register(() => constructed++);
        var before = services.ToArray();
        await Should.ThrowAsync<InvalidOperationException>(() => services.AddFluxFlowApplicationAsync(config).AsTask());
        services.ToArray().ShouldBe(before);
        constructed.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Link_parameters_preserve_nested_values_and_snapshot_at_registration(bool jsonText)
    {
        const string parameters = """
        {"minimum":5,"enabled":true,"settings":{"limit":12},"items":[1,"word"],"text":"\"123\""}
        """;
        using var config = Read("{\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\",\"Output\":{\"Port\":\"sink.Input\",\"Condition\":\"positive\",\"Parameters\":" +
            (jsonText ? System.Text.Json.JsonSerializer.Serialize(parameters) : parameters) +
            "}},\"sink\":{\"Type\":\"sink\"}}}}");
        FlowMapContext? observed = null;
        var engine = new Engine { Observe = context => observed = context };
        var services = Register(() => { });
        services.AddSingleton<IFlowExpressionEngine>(engine);
        await services.AddFluxFlowApplicationAsync(config);
        config["Workflows:main:source:Output:Parameters"] = "{\"minimum\":100}";
        config["Workflows:main:source:Output:Parameters:minimum"] = "100";
        await using var provider = services.BuildServiceProvider();
        var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var source = provider.GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var sink = provider.GetRequiredKeyedService<IFlowNode>("main.sink").ShouldBeOfType<Sink>();
        var message = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(6)));
        await source.Input.SendAsync(message);
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        observed.ShouldNotBeNull();
        observed.Variables["minimum"].ShouldBe(5);
        observed.Variables["enabled"].ShouldBe(true);
        ((System.Text.Json.JsonElement)observed.Variables["settings"]!).GetProperty("limit").GetInt32().ShouldBe(12);
        ((System.Text.Json.JsonElement)observed.Variables["items"]!)[1].GetString().ShouldBe("word");
        observed.Variables["text"].ShouldBe(jsonText ? "\"123\"" : "123");
        await app.StopAsync();
    }

    private static ServiceCollection Register(Action constructed)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ComponentRegistration>("source", (registrations, section) =>
            registrations.AddComponentFactory(section.GetComponentAddress(), _ => { constructed(); return new Pass(); })
                .AddOutputPort<Pass>(section.GetComponentAddress(), "Output", node => node.Output));
        services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) =>
            registrations.AddComponentFactory(section.GetComponentAddress(), _ => { constructed(); return new Sink(); })
                .AddInputPort<Sink>(section.GetComponentAddress(), "Input", node => node.Input));
        return services;
    }

    private static Microsoft.Extensions.Configuration.ConfigurationRoot Read(string json)
        => (Microsoft.Extensions.Configuration.ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();

    private sealed class Pass : FlowNode
    {
        protected override Task ProcessAsync(FlowMessage message) => EmitAsync(message).AsTask();
    }

    private sealed class Sink : IFlowNode
    {
        public BufferBlock<FlowMessage> Received { get; } = new();
        public ActionBlock<FlowMessage> Input { get; }
        public Sink() => Input = new(message => Received.Post(message));
        public Task Completion => Input.Completion;
        public void Complete() => Input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)Input).Fault(error);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }

    private sealed class Engine : IFlowExpressionEngine
    {
        public string Name => "rules";
        public int Compilations { get; private set; }
        public Action<FlowMapContext>? Observe { get; init; }
        public object? Evaluate(string expression, FlowMapContext context, Type resultType) => throw new NotSupportedException();
        public Func<FlowMapContext, T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
        {
            expression.ShouldBe("positive");
            Compilations++;
            return context =>
            {
                Observe?.Invoke(context);
                return (T)(object)(((FlowValue)context.Variables["input"]!).Json.GetInt32() >
                    (context.Variables.TryGetValue("minimum", out var minimum) ? Convert.ToInt32(minimum) : 0));
            };
        }
    }
}
