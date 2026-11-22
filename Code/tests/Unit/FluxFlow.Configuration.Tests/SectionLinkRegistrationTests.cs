using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application.Links;
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
    [InlineData("Input")]
    [InlineData("Output")]
    [InlineData("Error")]
    public async Task Single_bound_links_preserve_conditions_filters_and_plural_duplicate_identity(string direction)
    {
        using var config = Read("""
        {"Workflows":{"main":{"source":{"Type":"source"},"sink":{"Type":"sink"}}}}
        """);
        var section = config.GetSection(direction == "Input" ? "Workflows:main:sink" : "Workflows:main:source");
        section["Destination:Address"] = direction == "Input" ? "source.Output" : "sink.Input";
        section["Destination:Condition"] = "headers.site == \"SE01\"";
        section["Destination:Engine"] = "csharp";
        var services = Register(() =>
        {
        });
        services.AddCSharpExpressionEngine();
        services.AddFluxFlowApplication(config);
        var address = section.GetComponentAddress();
        var single = section.ReadRequiredConfiguration<SingleLinkConfiguration>().Destination!;
        var many = OneOrManyPortReferences.Read(section.GetSection("Destination"), address);
        if (direction == "Input")
            services.AddInputLink(address.Port("Input"), single).AddInputLinks(address.Port("Input"), many);
        else if (direction == "Output")
            services.AddOutputLink(address.Port("Output"), single).AddOutputLinks(address.Port("Output"), many);
        else
            services.AddErrorLink(address.Port("Output"), single).AddErrorLinks(address.Port("Output"), many);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var output = provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.source.Output").ShouldBeOfType<BroadcastBlock<FlowMessage>>();
        FlowMessage Message(string site, bool error) => FlowMessage.Create(error ? new FlowError("failed", "Failure", "test") : (FlowValue)"normal", headers: new Dictionary<string, FlowValue> { ["site"] = site });
        await output.SendAsync(Message("ignored", direction == "Error"));
        if (direction == "Error")
            await output.SendAsync(Message("SE01", false));
        var selected = Message("SE01", direction == "Error");
        await output.SendAsync(selected);
        var sink = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.sink").ShouldBeOfType<Sink>();
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(selected);
        await app.StopAsync();
        sink.Received.Count.ShouldBe(0);
    }

    public sealed class SingleLinkConfiguration
    {
        public PortReference? Destination { get; init; }
    }

    [Fact]
    public async Task Shared_link_registration_preserves_input_when_configuration_is_seen_through_its_output_base()
    {
        using var config = Read("""
        {"Workflows":{"main":{"source":{"Type":"source"},"sink":{"Type":"sink"}}}}
        """);
        var services = Register(() =>
        {
        });
        services.AddFluxFlowApplication(config);
        config["Workflows:main:sink:Input"] = "source.Output";
        OutputComponentConfiguration links = config.GetSection("Workflows:main:sink").ReadConfiguration<FluxFlow.Configuration.Components.Mapping.MapperConfiguration>()!;
        services.AddLinks(ComponentAddress.Create("main", "sink"), links);
        services.AddLink(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.sink.Input"));
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var source = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var sink = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.sink").ShouldBeOfType<Sink>();
        var message = FlowMessage.Create((FlowValue)"shared input");
        await source.Input.SendAsync(message);
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        await app.StopAsync();
        sink.Received.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Direct_typed_link_is_deferred_and_shares_identity_with_both_configuration_directions(bool configured, bool directFirst)
    {
        using var config = Read("""
        {"Workflows":{"main":{"source":{"Type":"source"},"sink":{"Type":"sink"}}}}
        """);
        if (configured)
        {
            config["Workflows:main:source:Output"] = "sink.Input";
            config["Workflows:main:sink:Input"] = "source.Output";
        }

        var constructed = 0;
        var services = Register(() => constructed++);
        var sourceAddress = ComponentAddress.Create("main", "source").Port("Output");
        var targetAddress = ComponentAddress.Create("main", "sink").Port("Input");
        if (directFirst)
            services.AddLink(sourceAddress, targetAddress);
        services.AddFluxFlowApplication(config);
        services.AddLink(sourceAddress, targetAddress);
        services.AddLink(PortAddress.Parse(sourceAddress.Value), PortAddress.Parse(targetAddress.Value));
        constructed.ShouldBe(0);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && item.IsKeyedService).ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        constructed.ShouldBe(2);
        await app.StartAsync();
        var source = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var sink = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.sink").ShouldBeOfType<Sink>();
        var message = FlowMessage.Create((FlowValue)"single link");
        await source.Input.SendAsync(message);
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        await app.StopAsync();
        sink.Received.Count.ShouldBe(0);
    }

    [Fact]
    public void Direct_link_rejects_a_system_destination_without_mutating_services()
    {
        var services = new ServiceCollection();
        Should.Throw<JsonException>(() => services.AddLink(PortAddress.Parse("main.source.Output"), PortAddress.Parse("System.Events.Output")));
        services.ShouldBeEmpty();
    }

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
        using var config = Read("{\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\",\"Output\":{\"Address\":\"sink.Input\",\"Condition\":\"" + expression + "\"" + engineField + "}},\"sink\":{\"Type\":\"sink\"}}}}");
        var services = Register(() =>
        {
        });
        services.AddCSharpExpressionEngine(useAsDefault: defaultEngine == "csharp").AddJsonataExpressionEngine(useAsDefault: defaultEngine == "jsonata");
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var source = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var sink = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.sink").ShouldBeOfType<Sink>();
        var message = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(new { number = 6 })));
        await source.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(new { number = 1 }))));
        await source.Input.SendAsync(message);
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        await app.StopAsync();
        sink.Received.Count.ShouldBe(0);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
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
        var services = Register(() =>
        {
        });
        services.AddKeyedSingleton<ITargetBlock<FlowMessage>>(hostKey, (_, _) => throw new InvalidOperationException("Host input must not be resolved."));
        services.AddKeyedSingleton<ISourceBlock<FlowMessage>>(hostKey, (_, _) => throw new InvalidOperationException("Host output must not be resolved."));
        services.AddFluxFlowApplication(config);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && item.IsKeyedService).ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var source = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var sink = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.sink").ShouldBeOfType<Sink>();
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
            "Output":[{"Address":"other.a.Input","Condition":"positive","Engine":"rules"},
                      {"Address":"other.b.Input","Condition":"positive","Engine":"rules"}]}},
          "other":{"a":{"Type":"sink",
            "Input":{"Address":"main.source.Output","Condition":"positive","Engine":"rules"}},
            "b":{"Type":"sink"}}
        }}
        """);
        var constructed = 0;
        var engine = new Engine();
        var services = Register(() => constructed++);
        services.AddKeyedSingleton<IFlowExpressionEngine>("rules", engine);
        services.AddFluxFlowApplication(config);
        constructed.ShouldBe(0);
        engine.Compilations.ShouldBe(0);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(2);
        var keyedLinks = services.Where(item => item.ServiceType == typeof(LinkFactory) && item.IsKeyedService).ToArray();
        keyedLinks.Length.ShouldBe(2);
        config["workflows:main:source:Output:0:Condition"] = "changed after registration";
        config["workflows:main:source:Output:0:Engine"] = "changed after registration";
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        constructed.ShouldBe(3);
        var executingLinks = provider.GetServices<LinkFactory>().ToArray();
        executingLinks.Length.ShouldBe(2);
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
        var source = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var positive = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(1)));
        var negative = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(-1)));
        await source.Input.SendAsync(positive);
        await source.Input.SendAsync(negative);
        var sinks = new List<Sink>();
        foreach (var name in new[]
        {
            "a",
            "b"
        }

        )
        {
            var sink = provider.GetWorkflowServices("other").GetRequiredKeyedService<IFlowNode>("other." + name).ShouldBeOfType<Sink>();
            sinks.Add(sink);
            (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(positive);
        }

        await app.StopAsync();
        foreach (var sink in sinks) sink.Received.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "rules")]
    [InlineData(true, "rules")]
    public async Task Both_link_directions_snapshot_distinct_conditions(bool inputSide, string? engineKey)
    {
        var engineField = engineKey is null ? "" : ",\"Engine\":\"rules\"";
        string Link(string port, int minimum) => "{\"Address\":\"" + port + "\",\"Condition\":\"value > " + minimum + "\"" + engineField + "}";
        var sourceLinks = inputSide ? "" : ",\"Output\":[" + Link("a.Input", 5) + "," + Link("b.Input", 6) + "]";
        var inputA = inputSide ? ",\"Input\":" + Link("source.Output", 5) : "";
        var inputB = inputSide ? ",\"Input\":" + Link("source.Output", 6) : "";
        using var config = Read("{\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\"" + sourceLinks + "},\"a\":{\"Type\":\"sink\"" + inputA + "},\"b\":{\"Type\":\"sink\"" + inputB + "}}}}");
        var services = Register(() =>
        {
        });
        var engine = new Engine();
        if (engineKey is null)
            services.AddSingleton<IFlowExpressionEngine>(engine);
        else
            services.AddKeyedSingleton<IFlowExpressionEngine>(engineKey, engine);
        services.AddFluxFlowApplication(config);
        config[inputSide ? "Workflows:main:a:Input:Condition" : "Workflows:main:source:Output:0:Condition"] = "value > 100";
        engine.Compilations.ShouldBe(0);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        engine.Compilations.ShouldBe(2);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
        engine.Compilations.ShouldBe(2);
        await app.StartAsync();
        var source = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.source").ShouldBeOfType<Pass>();
        var message = FlowMessage.Create(new JsonFlowValue(System.Text.Json.JsonSerializer.SerializeToElement(6)));
        await source.Input.SendAsync(message);
        var a = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.a").ShouldBeOfType<Sink>();
        (await a.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        var b = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.b").ShouldBeOfType<Sink>();
        await app.StopAsync();
        b.Received.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_explicit_engine_does_not_fall_back_to_default()
    {
        using var config = Read("""
        {"Workflows":{"main":{"source":{"Type":"source","Output":{"Address":"sink.Input","Condition":"positive","Engine":"missing"}},"sink":{"Type":"sink"}}}}
        """);
        var engine = new Engine();
        var services = Register(() =>
        {
        });
        services.AddSingleton<IFlowExpressionEngine>(engine);
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
        error.Message.ShouldContain("missing");
        engine.Compilations.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conditions_reuse_compilation_or_failure_only_within_their_provider(bool failCompilation)
    {
        using var config = Read("""
        {"Workflows":{"main":{
          "source":{"Type":"source","Output":[
            {"Address":"a.Input","Condition":"positive","Engine":"rules"},
            {"Address":"b.Input","Condition":"positive","Engine":"rules"}],
            "Error":{"Address":"b.Input","Condition":"positive","Engine":"rules"}},
          "a":{"Type":"sink"},"b":{"Type":"sink"}
        }}}
        """);
        var services = Register(() =>
        {
        });
        var constructions = 0;
        services.AddKeyedSingleton<IFlowExpressionEngine>("rules", (_, _) =>
        {
            constructions++;
            return new Engine
            {
                Failure = failCompilation ? new ArgumentException("Invalid condition.") : null
            };
        });
        services.AddFluxFlowApplication(config);
        constructions.ShouldBe(0);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(3);
        for (var index = 0; index < 2; index++)
        {
            await using var provider = services.BuildServiceProvider();
            if (failCompilation)
            {
                await Should.ThrowAsync<ArgumentException>(() => provider.BuildApplicationAsync());
                var engine = provider.GetRequiredKeyedService<IFlowExpressionEngine>("rules").ShouldBeOfType<Engine>();
                foreach (var link in provider.GetServices<LinkFactory>())
                    Should.Throw<ArgumentException>(() => link(provider)).ShouldBeSameAs(engine.Failure);
            }
            else
            {
                await using var app = await provider.BuildApplicationAsync();
                (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
            }

            provider.GetRequiredKeyedService<IFlowExpressionEngine>("rules").ShouldBeOfType<Engine>().Compilations.ShouldBe(1);
            constructions.ShouldBe(index + 1);
        }
    }

    [Fact]
    public async Task Identical_condition_text_keeps_default_and_explicit_engine_compilations_separate()
    {
        using var config = Read("""
        {"Workflows":{"main":{
          "source":{"Type":"source","Output":[
            {"Address":"a.Input","Condition":"positive"},
            {"Address":"b.Input","Condition":"positive","Engine":"rules"},
            {"Address":"c.Input","Condition":"positive","Engine":"other"}]},
          "a":{"Type":"sink"},"b":{"Type":"sink"},"c":{"Type":"sink"}
        }}}
        """);
        var defaultEngine = new Engine();
        var rules = new Engine();
        var other = new Engine();
        var services = Register(() =>
        {
        });
        services.AddSingleton<IFlowExpressionEngine>(defaultEngine);
        services.AddKeyedSingleton<IFlowExpressionEngine>("rules", rules);
        services.AddKeyedSingleton<IFlowExpressionEngine>("other", other);
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        defaultEngine.Compilations.ShouldBe(1);
        rules.Compilations.ShouldBe(1);
        other.Compilations.ShouldBe(1);
    }

    [Theory]
    [InlineData("123", null)]
    [InlineData("[[\"sink.Input\"]]", null)]
    [InlineData("{\"Address\":[\"sink.Input\"]}", null)]
    [InlineData("{\"Port\":\"sink.Input\"}", null)]
    [InlineData("{\"Address\":\"sink.Input\",\"Condition\":{\"value\":\"positive\"}}", null)]
    [InlineData("{\"Address\":\"sink.Input\",\"Fallback\":true,\"Condition\":\"positive\"}", null)]
    [InlineData("{\"Address\":\"sink.Input\",\"Fallback\":false}", null)]
    [InlineData("{\"Address\":\"sink.Input\",\"Delivery\":\"Reliable\"}", null)]
    [InlineData("{\"Address\":\"sink.Input\",\"Delivery\":\"RejectWhenFull\"}", null)]
    [InlineData("{\"Address\":\"sink.Input\",\"Delivery\":\"IsolateRejection\"}", null)]
    [InlineData("{\"Address\":\"sink.Input\",\"Unknown\":true}", null)]
    [InlineData("{\"Address\":\"sink.Input\",\"Engine\":\"rules\"}", null)]
    [InlineData("{\"Address\":\"sink.Input\",\"Condition\":\"positive\",\"Engine\":\" \"}", null)]
    [InlineData("\"sink.Input\"", "{\"Address\":\"source.Output\",\"Delivery\":\"Reliable\"}")]
    public void Invalid_links_reject_registration_atomically(string output, string? input)
    {
        using var config = Read("{\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\",\"Output\":" + output + "},\"sink\":{\"Type\":\"sink\"" + (input is null ? "" : ",\"Input\":" + input) + "}}}}");
        var constructed = 0;
        var services = Register(() => constructed++);
        var before = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(config));
        if (output.Contains("Delivery") || input?.Contains("Delivery") == true)
            error.Message.ShouldContain("unknown property 'Delivery'");
        services.ToArray().ShouldBe(before);
        constructed.ShouldBe(0);
    }

    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "{\"minimum\":5}")]
    public void Removed_parameters_are_rejected_with_the_port_location(bool inputSide, string parameters)
    {
        var link = "{\"Address\":\"" + (inputSide ? "source.Output" : "sink.Input") + "\",\"Condition\":\"positive\",\"Parameters\":" + parameters + "}";
        using var config = Read("{\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\"" + (inputSide ? "" : ",\"Output\":" + link) + "},\"sink\":{\"Type\":\"sink\"" + (inputSide ? ",\"Input\":" + link : "") + "}}}}");
        var services = Register(() => throw new InvalidOperationException("Must not construct components."));
        var before = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(config));
        error.Message.ShouldContain("unknown property 'Parameters'");
        error.Message.ShouldContain(inputSide ? "Workflows:main:sink:Input" : "Workflows:main:source:Output");
        services.ToArray().ShouldBe(before);
    }

    [Theory]
    [InlineData("System.Events.Output", false)]
    [InlineData("System.Events.Output", true)]
    [InlineData("System.Diagnostics.Output", false)]
    [InlineData("System.Diagnostics.Output", true)]
    public void System_outputs_are_allowed_as_references_only_on_the_source_side(string address, bool inputSide)
    {
        using var config = Read(inputSide ? "{\"Workflows\":{\"main\":{\"sink\":{\"Type\":\"sink\",\"Input\":\"" + address + "\"}}}}" : "{\"Workflows\":{\"main\":{\"source\":{\"Type\":\"source\",\"Output\":\"" + address + "\"}}}}");
        var services = Register(() => throw new InvalidOperationException("Registration must not construct components."));
        var before = services.ToArray();
        if (inputSide)
        {
            // This accepts the address; Build still requires an actual registered source block.
            services.AddFluxFlowApplication(config);
            services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(1);
        }
        else
        {
            var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(config));
            error.Message.ShouldContain($"System output '{address}' cannot be a link destination.");
            error.Message.ShouldContain("Workflows:main:source");
            services.ToArray().ShouldBe(before);
        }
    }

    private static ServiceCollection Register(Action constructed)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ComponentRegistration>("source", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<TestPortConfiguration>();
            registrations.AddComponentFactory(ComponentAddress.Parse(address.Value), _ =>
            {
                constructed();
                return new Pass();
            }).AddOutputPort<Pass>(ComponentAddress.Parse(address.Value), "Output", node => node.Output).AddOutputLinks(address.Port("Output"), config.Output).AddErrorLinks(address.Port("Output"), config.Error);
        });
        services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) => registrations.AddComponentFactory(section.GetComponentAddress(), _ =>
        {
            constructed();
            return new Sink();
        }).AddInputPort<Sink>(section.GetComponentAddress(), "Input", node => node.Input).AddInputLinks(section.GetComponentAddress().Port("Input"), section.ReadConfiguration<TestPortConfiguration>()!.Input));
        return services;
    }

    private static Microsoft.Extensions.Configuration.ConfigurationRoot Read(string json) => (Microsoft.Extensions.Configuration.ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
    private sealed class Pass : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Pass()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        private Task ProcessAsync(FlowMessage message) => EmitAsync(message).AsTask();
    }

    private sealed class Sink : IFlowNode
    {
        public BufferBlock<FlowMessage> Received { get; } = new();
        public ActionBlock<FlowMessage> Input { get; }

        public Sink() => Input = new(message => Received.Post(message));
        public Task Completion => Input.Completion;

        public void Complete() => Input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)Input).Fault(error);
        public async ValueTask DisposeAsync()
        {
            Complete();
            await Completion;
        }
    }

    private sealed class Engine : IFlowExpressionEngine
    {
        public string Name => "rules";
        public int Compilations { get; private set; }
        public Exception? Failure { get; init; }

        public object? Evaluate(string expression, FlowMapContext context, Type resultType) => throw new NotSupportedException();
        public Func<FlowMapContext, T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
        {
            Compilations++;
            if (Failure is not null)
                throw Failure;
            var minimum = expression == "positive" ? 0 : int.Parse(expression["value > ".Length..]);
            return context =>
            {
                return (T)(object)(((FlowValue)context.Variables["input"]!).Json.GetInt32() > minimum);
            };
        }
    }
}
