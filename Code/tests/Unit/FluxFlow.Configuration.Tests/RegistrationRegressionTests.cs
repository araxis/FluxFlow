using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing;
using FluxFlow.Configuration.Components.Batching;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Routing;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Configuration.Components.Sources;
using FluxFlow.Application;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.CSharp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;
public sealed class RegistrationRegressionTests
{
    [Theory]
    [InlineData("open", true)]
    [InlineData("OPEN", true)]
    [InlineData("close", false)]
    [InlineData("CLOSE", false)]
    public async Task Gate_control_properties_use_configuration_casing(string port, bool expectedOpen)
    {
        using var config = Read(new() { ["Workflows:main:driver:Type"] = "probe", ["Workflows:main:gate:Type"] = "gate", ["Workflows:main:gate:InitiallyOpen"] = (!expectedOpen).ToString(), [$"Workflows:main:gate:{port}"] = "driver.Output" });
        var services = RegisterProbe(new ServiceCollection().AddGate());
        services.AddFluxFlowApplication(config);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var gate = (Gate)provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.gate");
        var driver = provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.driver.Input");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await driver.SendAsync(FlowMessage.Create(JsonFlowValue.Null), timeout.Token);
        while (gate.IsOpen != expectedOpen)
            await Task.Delay(1, timeout.Token);
        await app.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData("priority", true)]
    [InlineData("PRIORITY", true)]
    [InlineData("unmatched", false)]
    [InlineData("UNMATCHED", false)]
    public async Task Router_output_properties_use_configuration_casing(string port, bool matches)
    {
        using var config = Read(new() { ["Workflows:main:route:Type"] = "router", ["Workflows:main:route:Engine"] = "csharp", ["Workflows:main:route:Routes:Priority"] = matches ? "true" : "false", [$"Workflows:main:route:{port}"] = "sink.Input", ["Workflows:main:sink:Type"] = "probe" });
        var services = RegisterProbe(new ServiceCollection().AddRouter().AddCSharpExpressionEngine());
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var message = FlowMessage.Create((FlowValue)"payload");
        await provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.route.Input").SendAsync(message);
        var sink = (Probe)provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.sink");
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("scheduler", false)]
    [InlineData("scheduler", true)]
    [InlineData("gate", false)]
    [InlineData("gate", true)]
    [InlineData("source", false)]
    [InlineData("source", true)]
    [InlineData("mapper", false)]
    [InlineData("mapper", true)]
    [InlineData("router", false)]
    [InlineData("router", true)]
    [InlineData("batch", false)]
    [InlineData("batch", true)]
    public async Task Bundled_events_are_routable_and_drain_before_provider_disposal(string type, bool inputSide)
    {
        var values = new Dictionary<string, string?>
        {
            ["Workflows:main:node:Type"] = type,
            ["Workflows:main:sink:Type"] = "probe",
            [inputSide ? "Workflows:main:sink:Input" : "Workflows:main:node:events"] = inputSide ? "node.Events" : "sink.Input"
        };
        switch (type)
        {
            case "scheduler":
                values["Workflows:main:node:Interval"] = "01:00:00";
                break;
            case "source":
                values["Workflows:main:node:Values"] = "[1]";
                break;
            case "mapper":
                values["Workflows:main:node:Engine"] = "csharp";
                values["Workflows:main:node:Expression"] = "value";
                break;
            case "router":
                values["Workflows:main:node:Engine"] = "csharp";
                values["Workflows:main:node:Routes:Selected"] = "true";
                break;
            case "batch":
                values["Workflows:main:node:BatchSize"] = "2";
                values["Workflows:main:node:MaxWait"] = "00:00:05";
                break;
        }

        using var config = Read(values);
        var services = RegisterProbe(new ServiceCollection().AddScheduler().AddGate().AddSource().AddMapper().AddRouter().AddBatch().AddCSharpExpressionEngine());
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Events").ShouldNotBeNull();
        await app.StartAsync();
        var node = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.node");
        var events = ((FlowNode)node).Events;
        // Bundled components expose native BroadcastBlocks; inject an event through that block.
        var message = FlowMessage.Create((FlowValue)"event", headers: new Dictionary<string, FlowValue> { ["custom"] = "retained" });
        ((ITargetBlock<FlowMessage>)events).Post(message).ShouldBeTrue();
        var received = await ((Probe)provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.sink")).Received.ReceiveAsync(TimeSpan.FromSeconds(10));
        received.MessageId.ShouldBe(message.MessageId);
        received.Value.ShouldBeSameAs(message.Value);
        received.Headers["custom"].ShouldBeSameAs(message.Headers["custom"]);
        received.Headers[FlowEventHeaders.Source].Json.GetString().ShouldBe("main.node");
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("mapper", "RulesV1", true)]
    [InlineData("router", "RulesV1", true)]
    [InlineData("link", "RulesV1", true)]
    [InlineData("mapper", " RulesV1 ", true)]
    [InlineData("router", " RulesV1 ", true)]
    [InlineData("link", " RulesV1 ", true)]
    [InlineData("mapper", "rulesv1", false)]
    [InlineData("router", "rulesv1", false)]
    [InlineData("link", "rulesv1", false)]
    public async Task Explicit_engine_keys_are_preserved_without_fallback(string consumer, string key, bool registered)
    {
        var values = new Dictionary<string, string?>
        {
            ["Workflows:main:node:Type"] = consumer == "link" ? "probe" : consumer,
            ["Workflows:main:sink:Type"] = "probe"
        };
        if (consumer == "link")
        {
            values["Workflows:main:node:Output:Address"] = "sink.Input";
            values["Workflows:main:node:Output:Condition"] = "true";
            values["Workflows:main:node:Output:Engine"] = key;
        }
        else
        {
            values["Workflows:main:node:Engine"] = key;
            values[consumer == "mapper" ? "Workflows:main:node:Expression" : "Workflows:main:node:Routes:Selected"] = consumer == "mapper" ? "value" : "true";
            values[consumer == "mapper" ? "Workflows:main:node:Output" : "Workflows:main:node:Selected"] = "sink.Input";
        }

        using var config = Read(values);
        var services = RegisterProbe(new ServiceCollection().AddMapper().AddRouter().AddCSharpExpressionEngine(useAsDefault: true));
        services.AddKeyedSingleton<IFlowExpressionEngine>(registered ? key : "RulesV1", (provider, _) => provider.GetRequiredKeyedService<IFlowExpressionEngine>("csharp"));
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        if (!registered)
        {
            await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
            return;
        }

        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var message = FlowMessage.Create((FlowValue)"payload");
        await provider.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input").SendAsync(message);
        var received = await ((Probe)provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.sink")).Received.ReceiveAsync(TimeSpan.FromSeconds(10));
        received.Value.Json.GetString().ShouldBe("payload");
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("mapper", false)]
    [InlineData("router", false)]
    [InlineData("link", false)]
    [InlineData("mapper", true)]
    [InlineData("router", true)]
    [InlineData("link", true)]
    public async Task Expression_preparation_preserves_compile_contracts_and_rejects_missing_delegates(string consumer, bool missingDelegate)
    {
        var values = new Dictionary<string, string?>
        {
            ["Workflows:main:node:Type"] = consumer == "link" ? "probe" : consumer,
            ["Workflows:main:sink:Type"] = "probe"
        };
        if (consumer == "link")
        {
            values["Workflows:main:node:Output:Address"] = "sink.Input";
            values["Workflows:main:node:Output:Condition"] = "condition";
            values["Workflows:main:node:Output:Engine"] = "rules";
        }
        else
        {
            values["Workflows:main:node:Engine"] = "rules";
            values[consumer == "mapper" ? "Workflows:main:node:Expression" : "Workflows:main:node:Routes:First"] = "expression";
            if (consumer == "router")
                values["Workflows:main:node:Routes:Second"] = "another expression";
        }

        using var config = Read(values);
        var engine = new CompileContractEngine(missingDelegate);
        var resolutions = 0;
        var services = RegisterProbe(new ServiceCollection().AddMapper().AddRouter());
        services.AddKeyedTransient<IFlowExpressionEngine>("rules", (_, _) =>
        {
            resolutions++;
            return engine;
        });
        services.AddFluxFlowApplication(config);
        resolutions.ShouldBe(0);
        engine.NormalCompilations.ShouldBe(0);
        engine.HintedCompilations.ShouldBe(0);
        await using var provider = services.BuildServiceProvider();
        if (missingDelegate)
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync());
            error.Message.ShouldContain("Expression engine 'rules' returned no compiled expression.");
            if (consumer == "mapper")
                error.Message.ShouldContain("Mapper 'main.node'");
            if (consumer == "router")
                error.Message.ShouldContain("Router 'main.node' route 'First'");
        }
        else
        {
            await using var app = await provider.BuildApplicationAsync();
            await provider.BuildApplicationAsync();
        }

        resolutions.ShouldBe(1);
        engine.NormalCompilations.ShouldBe(consumer == "link" ? 0 : consumer == "router" && !missingDelegate ? 2 : 1);
        engine.HintedCompilations.ShouldBe(consumer == "link" ? 1 : 0);
    }

    private sealed class CompileContractEngine(bool missingDelegate) : IFlowExpressionEngine
    {
        public string Name => "rules";
        public int NormalCompilations { get; private set; }
        public int HintedCompilations { get; private set; }

        public object? Evaluate(string expression, FlowMapContext context, Type resultType) => throw new NotSupportedException();
        public Func<FlowMapContext, T> Compile<T>(string expression)
        {
            NormalCompilations++;
            return missingDelegate ? null! : _ => throw new InvalidOperationException("Build must not evaluate expressions.");
        }

        public Func<FlowMapContext, T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
        {
            HintedCompilations++;
            parameterTypes.Count.ShouldBe(3);
            parameterTypes["input"].ShouldBe(typeof(FlowValue));
            parameterTypes["payload"].ShouldBe(typeof(FlowValue));
            parameterTypes["message"].ShouldBe(typeof(FlowMessage));
            return missingDelegate ? null! : _ => throw new InvalidOperationException("Build must not evaluate expressions.");
        }
    }

    private static ConfigurationRoot Read(Dictionary<string, string?> values) => (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static IServiceCollection RegisterProbe(IServiceCollection services) => services.AddKeyedSingleton<ComponentRegistration>("probe", (registrations, section) =>
    {
        var address = section.GetComponentAddress();
        var config = section.ReadConfiguration<TestPortConfiguration>()!;
        registrations.AddComponentFactory(ComponentAddress.Parse(address.Value), _ => new Probe()).AddInputPort<Probe>(ComponentAddress.Parse(address.Value), "Input", node => node.Input).AddOutputPort<Probe>(ComponentAddress.Parse(address.Value), "Output", node => node.Output).AddInputLinks(address.Port("Input"), config.Input).AddOutputLinks(address.Port("Output"), config.Output);
    });
    private sealed class Probe : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Probe()
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

        public BufferBlock<FlowMessage> Received { get; } = new();

        private async Task ProcessAsync(FlowMessage message)
        {
            Received.Post(message);
            await EmitAsync(message);
        }
    }
}
