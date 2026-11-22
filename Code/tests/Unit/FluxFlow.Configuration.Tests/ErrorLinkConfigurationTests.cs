using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Configuration.Components.Batching;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Routing;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Configuration.Components.Sources;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;
public sealed class ErrorLinkConfigurationTests
{
    [Theory]
    [InlineData("scheduler")]
    [InlineData("source")]
    [InlineData("gate")]
    [InlineData("mapper")]
    [InlineData("router")]
    [InlineData("batch")]
    public async Task Error_filters_existing_output_without_a_new_port_and_preserves_output_behavior(string type)
    {
        using var config = Read(type);
        // A repeated declaration is one link; Output and Error remain separate selections.
        config["Workflows:main:node:error:0"] = "errors.Inbound";
        config["Workflows:main:node:error:1:Address"] = "main.errors.Inbound";
        config["Workflows:main:node:Output"] = "all.Inbound";
        var services = Services();
        services.AddFluxFlowApplication(config);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(2);
        // Link values are captured by the registrar, not read again at Build.
        config["Workflows:main:node:error:0"] = "missing.Input";
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        provider.GetWorkflowServices("main").GetKeyedService<ISourceBlock<FlowMessage>>("main.node.Error").ShouldBeNull();
        await app.StartAsync();
        var output = provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Output").ShouldBeOfType<BroadcastBlock<FlowMessage>>();
        var normal = FlowMessage.Create((FlowValue)"normal");
        var error = Error("failed");
        await output.SendAsync(normal);
        await output.SendAsync(error);
        var all = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.all").ShouldBeOfType<Sink>();
        var errors = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.errors").ShouldBeOfType<Sink>();
        (await all.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(normal);
        (await all.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(error);
        (await errors.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(error);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        all.Received.Count.ShouldBe(0);
        errors.Received.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("csharp", "value.Code == \"selected\" && headers.site == \"SE01\" && messageId != null")]
    [InlineData("jsonata", "$value.Code = 'selected' and $headers.site = 'SE01' and $exists($messageId)")]
    public async Task Error_conditions_receive_original_context_and_do_not_run_for_normal_values(string engine, string condition)
    {
        using var config = Read("gate");
        config["Workflows:main:node:Error:Address"] = "errors.Inbound";
        config["Workflows:main:node:Error:Condition"] = condition;
        config["Workflows:main:node:Error:Engine"] = engine;
        var services = Services();
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var output = (BroadcastBlock<FlowMessage>)provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Output");
        await output.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
        await output.SendAsync(Error("ignored"));
        var selected = Error("selected");
        await output.SendAsync(selected);
        var sink = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.errors").ShouldBeOfType<Sink>();
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(selected);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        sink.Received.Count.ShouldBe(0);
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Registrar_maps_its_configuration_property_to_a_native_port_without_central_discovery()
    {
        using var config = Read("gate");
        config["Workflows:main:errors:ReceiveFrom"] = "node.Output";
        var services = Services();
        services.AddFluxFlowApplication(config);
        services.Count(item => item.ServiceType == typeof(LinkFactory) && !item.IsKeyedService).ShouldBe(1);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        await app.StartAsync();
        var output = (BroadcastBlock<FlowMessage>)provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Output");
        var message = FlowMessage.Create((FlowValue)"input-side link");
        await output.SendAsync(message);
        var sink = provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.errors").ShouldBeOfType<Sink>();
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(message);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("", "errors.Inbound", false)]
    [InlineData("Condition", "true", false)]
    [InlineData("Address", "not-an-address", false)]
    [InlineData("Address", "absent.Input", true)]
    public async Task Invalid_error_links_fail_at_the_same_stage_as_normal_links(string field, string value, bool build)
    {
        using var config = Read("gate");
        // Unknown fields and missing/invalid addresses are rejected during Add.
        config[$"Workflows:main:node:Error:{(field.Length == 0 ? "Unexpected" : field)}"] = value;
        var services = Services();
        if (!build)
        {
            var before = services.ToArray();
            Should.Throw<Exception>(() => services.AddFluxFlowApplication(config));
            services.ToArray().ShouldBe(before);
            return;
        }

        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await Should.ThrowAsync<Exception>(() => provider.BuildApplicationAsync());
    }

    private static FlowMessage Error(string code) => FlowMessage.Create(new FlowError(code, "Request failed", "test"), headers: new Dictionary<string, FlowValue> { ["site"] = "SE01" });
    private static ConfigurationRoot Read(string type)
    {
        var values = new Dictionary<string, string?>
        {
            ["Workflows:main:node:Type"] = type,
            ["Workflows:main:errors:Type"] = "sink",
            ["Workflows:main:all:Type"] = "sink"
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

        return (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IServiceCollection Services() => new ServiceCollection().AddScheduler().AddSource().AddGate().AddMapper().AddRouter().AddBatch().AddCSharpExpressionEngine().AddJsonataExpressionEngine().AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) =>
    {
        var address = section.GetComponentAddress();
        var config = section.ReadConfiguration<SinkConfiguration>()!;
        registrations.AddComponentFactory(ComponentAddress.Parse(address.Value), _ => new Sink()).AddInputPort<Sink>(ComponentAddress.Parse(address.Value), "Inbound", node => node.Input).AddInputLinks(address.Port("Inbound"), config.ReceiveFrom);
    });
    private sealed class SinkConfiguration
    {
        public OneOrManyPortReferences ReceiveFrom { get; init; } = OneOrManyPortReferences.Empty;
    }

    private sealed class Sink : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Sink()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping, handlesErrors: true), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public BufferBlock<FlowMessage> Received { get; } = new();

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            Received.Post(message);
            return Task.FromResult<IEnumerable<FlowMessage>>([]);
        }
    }
}
