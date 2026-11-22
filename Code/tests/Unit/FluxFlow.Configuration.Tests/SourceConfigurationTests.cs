using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Text.Json;
using FluxFlow.Components.Source;
using FluxFlow.Configuration.Components.Sources;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Dataflow;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;
public sealed class SourceConfigurationTests
{
    [Theory]
    [InlineData("[123,\"123\",true,\"true\",null,{},[],{\"code\":\"001\"}]")]
    [InlineData("[]")]
    public async Task Source_preserves_json_types_lineage_and_independent_hosts(string valuesJson)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["workflows:main:samples:type"] = "source", ["workflows:main:samples:values"] = valuesJson, ["workflows:main:samples:outputCapacity"] = "2", ["workflows:main:samples:output"] = "sink.Input", ["workflows:main:sink:type"] = "sink" }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var sinks = new List<Sink>();
        var services = new ServiceCollection().AddSource().AddSource();
        services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) => registrations.AddComponentFactory(section.GetComponentAddress(), _ =>
        {
            var sink = new Sink();
            sinks.Add(sink);
            return sink;
        }).AddInputPort<Sink>(section.GetComponentAddress(), "Input", node => node.Input));
        services.AddFluxFlowApplication(configuration);
        configuration["workflows:main:samples:values"] = "invalid after registration";
        sinks.ShouldBeEmpty();
        using var expected = JsonDocument.Parse(valuesJson);
        var values = expected.RootElement.EnumerateArray().ToArray();
        for (var run = 0; run < 2; run++)
        {
            await using var provider = services.BuildServiceProvider();
            sinks.Count.ShouldBe(run);
            await using var app = await provider.BuildApplicationAsync();
            var source = (Source)app.GetComponent(ComponentAddress.Parse("main.samples"));
            sinks[run].Received.ShouldBeEmpty();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await app.StartAsync(deadline.Token);
            var inputs = Enumerable.Range(0, values.Length + 1).Select(_ => FlowMessage.Create(JsonFlowValue.Null)).ToArray();
            foreach (var input in inputs)
                (await source.Input.SendAsync(input, deadline.Token)).ShouldBeTrue();
            await app.StopAsync(deadline.Token);
            sinks[run].Received.Count.ShouldBe(values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                var output = sinks[run].Received[index];
                output.Value.Json.ValueKind.ShouldBe(values[index].ValueKind);
                output.Value.Json.GetRawText().ShouldBe(values[index].GetRawText());
                output.CausationId.ShouldBe(inputs[index].MessageId);
                output.TraceId.ShouldBe(inputs[index].TraceId);
            }
        }
    }

    [Theory]
    [InlineData(null, 128)]
    [InlineData("", 128)]
    [InlineData("{}", 128)]
    [InlineData("[invalid", 128)]
    [InlineData("[]", 0)]
    public void Invalid_values_reject_before_construction(string? values, int capacity)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["workflows:main:samples:type"] = "source", ["workflows:main:samples:values"] = values, ["workflows:main:samples:outputCapacity"] = capacity.ToString() }).Build();
        using var configurationLifetime = (IDisposable)configuration;
        var services = new ServiceCollection().AddSource();
        var before = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(configuration));
        error.Message.ShouldContain("main.samples");
        services.ToArray().ShouldBe(before);
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
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public List<FlowMessage> Received { get; } = [];

        private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            Received.Add(message);
            return Task.FromResult<IEnumerable<FlowMessage>>([]);
        }
    }
}
