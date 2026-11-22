using System.Threading.Tasks.Dataflow;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;
public sealed class DirectConfigurationTests
{
    [Fact]
    public async Task Sections_retain_strings_and_register_deferred_factories_without_a_document_roundtrip()
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Host:Resources:Connections:shared:Type"] = "123", ["Host:Resources:Connections:shared:Value"] = "001", ["Host:Workflows:main:Resources:Connections:shared:Type"] = "123", ["Host:Workflows:main:Resources:Connections:shared:Value"] = "true", ["Host:Workflows:main:node:Type"] = "true", ["Host:Workflows:main:node:node:Unused"] = "anything", ["Host:Workflows:main:node:Resource"] = "connections.shared", ["Host:Workflows:main:node:Custom.Value"] = "[1,2]" }).Build();
        var services = new ServiceCollection();
        var resources = 0;
        var components = 0;
        var sections = new List<string>();
        services.AddKeyedSingleton<ResourceRegistration>("123", (registrations, address, section) =>
        {
            sections.Add(section.Path);
            var value = section.Get<ResourceConfiguration>()!.Value;
            registrations.AddFluxFlowResource(address, _ =>
            {
                resources++;
                return value;
            });
        });
        services.AddKeyedSingleton<ComponentRegistration>("true", (registrations, section) =>
        {
            section.Path.ShouldBe("Host:Workflows:main:node");
            section["Custom.Value"].ShouldBe("[1,2]");
            var resource = section["Resource"]!;
            registrations.AddComponentFactory(section.GetComponentAddress(), provider =>
            {
                components++;
                return new Pass(provider.GetRequiredFlowResource<string>(resource, "main"));
            });
        });
        services.AddFluxFlowApplication(configuration.GetSection("Host"));
        sections.ShouldBe(new[] { "Host:Resources:Connections:shared", "Host:Workflows:main:Resources:Connections:shared" });
        resources.ShouldBe(0);
        components.ShouldBe(0);
        configuration["Host:Workflows:main:Resources:Connections:shared:Value"] = "changed";
        configuration["Host:Workflows:main:node:Resource"] = "missing";
        await using var provider = services.BuildServiceProvider();
        await using var application = await provider.BuildApplicationAsync();
        provider.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.node").ShouldBeOfType<Pass>().Value.ShouldBe("true");
        provider.GetRequiredFlowResource<string>("connections.shared").ShouldBe("001");
        resources.ShouldBe(2);
        components.ShouldBe(1);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(application);
        components.ShouldBe(1);
    }

    [Theory]
    [InlineData("Application", "unexpected")]
    [InlineData("Workflows", "scalar")]
    [InlineData("Resources", "scalar")]
    [InlineData("ExpressionEngine:Nested", "invalid")]
    public void Invalid_structure_leaves_services_unchanged(string key, string value)
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Workflows:main"] = null, [key] = value }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(new object ());
        var before = services.ToArray();
        Should.Throw<CompositionConfigurationException>(() => services.AddFluxFlowApplication(configuration));
        services.ToArray().ShouldBe(before);
    }

    private sealed class Pass : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Pass(string value)
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
            Value = value;
            Lifetime.Observe();
        }

        internal string Value { get; }

        private Task ProcessAsync(FlowMessage message) => EmitAsync(message).AsTask();
    }

    private sealed class ResourceConfiguration
    {
        public string Value { get; set; } = "";
    }
}
