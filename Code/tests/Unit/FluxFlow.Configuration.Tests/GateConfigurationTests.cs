using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Routing;
using FluxFlow.Configuration.Components.Routing;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;
public sealed class GateConfigurationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Configured_gate_registers_native_ports_and_preserves_control_and_error_behavior(bool initiallyOpen, bool toggle)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($$"""
        { "workflows": {
          "main": { "gate": { "type": "gate", "initiallyOpen": {{initiallyOpen.ToString().ToLowerInvariant()}},
                      "outputCapacity": 2, "output": "other.sink.Input" } },
          "other": { "sink": { "type": "sink" } }
        } }
        """));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var received = new List<FlowMessage>();
        var services = new ServiceCollection().AddGate().AddGate();
        services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) => registrations.AddComponentFactory(section.GetComponentAddress(), _ => new Sink(received)).AddInputPort<Sink>(section.GetComponentAddress(), "Input", node => node.Input));
        services.AddFluxFlowApplication(configuration);
        configuration["workflows:main:gate:initiallyOpen"] = (!initiallyOpen).ToString();
        Gate? gate = null;
        services.AddSingleton<LinkFactory>(ports =>
        {
            gate = (Gate)ports.GetWorkflowServices("main").GetRequiredKeyedService<IFlowNode>("main.gate");
            ports.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.gate.Input").ShouldBeSameAs(gate.Input);
            ports.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.gate.Open").ShouldBeSameAs(gate.Open);
            ports.GetWorkflowServices("main").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.gate.Close").ShouldBeSameAs(gate.Close);
            ports.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.gate.Output").ShouldBeSameAs(gate.Output);
        });
        gate.ShouldBeNull();
        await using var provider = services.BuildServiceProvider();
        gate.ShouldBeNull();
        await using var app = await provider.BuildApplicationAsync();
        gate.ShouldNotBeNull().IsOpen.ShouldBe(initiallyOpen);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await app.StartAsync(deadline.Token);
        if (toggle)
        {
            await (initiallyOpen ? gate.Close : gate.Open).SendAsync(FlowMessage.Create(JsonFlowValue.Null), deadline.Token);
            while (gate.IsOpen == initiallyOpen)
                await Task.Delay(1, deadline.Token);
        }

        var messages = new[]
        {
            FlowMessage.Create(JsonFlowValue.Null),
            FlowMessage.Create<FlowValue>(new FlowError("test", "failure", "test"))
        };
        foreach (var message in messages)
            (await gate.Input.SendAsync(message, deadline.Token)).ShouldBeTrue();
        await app.StopAsync(deadline.Token);
        if (initiallyOpen != toggle)
            received.ShouldBe(messages);
        else
            received.ShouldBeEmpty();
        gate.Open.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        gate.Close.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData("outputCapacity", "0")]
    [InlineData("outputCapacity", "-1")]
    [InlineData("initiallyOpen", "invalid")]
    public void Invalid_settings_preserve_host_registrations(string property, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["workflows:main:gate:Type"] = "gate", [$"workflows:main:gate:{property}"] = value }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection().AddGate();
        var before = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(configuration));
        error.Message.ShouldContain("main.gate");
        services.ToArray().ShouldBe(before);
    }

    private sealed class Sink : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Sink(List<FlowMessage> received)
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
            this.received = received;
            Lifetime.Observe();
        }

        private readonly List<FlowMessage> received;
        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            received.Add(message);
            return Task.FromResult<IEnumerable<FlowMessage>>([]);
        }
    }
}
