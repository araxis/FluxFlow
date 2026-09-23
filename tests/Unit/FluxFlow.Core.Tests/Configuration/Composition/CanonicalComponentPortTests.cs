using FluxFlow.Testing;
using FluxFlow.Core.Application;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Reflection;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class CanonicalComponentPortTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Canonical_component_ports_preserve_native_identity_kind_and_postponement(
        bool resolveBeforeBuild, bool signal, bool error)
    {
        var source = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        var target = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        var node = new PortNode(source, target);
        await using var provider = new ServiceCollection().AddComponentFactory("main.node", _ => node)
                .AddInputPort<PortNode>("main.node", "Input", n => n.Input, signal: signal)
                .AddOutputPort<PortNode>("main.node", "Output", n => n.Output).BuildServiceProvider();
        if (resolveBeforeBuild)
        {
            provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input").ShouldBeSameAs(target);
            provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Output").ShouldBeSameAs(source);
        }
        await using var owner = await provider.BuildApplicationAsync();
        var input = provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.node.Input");
        var output = provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.node.Output");
        var value = ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { Text = "native-\u03bb", Values = new[] { 1, 2 }, Bytes = new byte[] { 0, 128, 255 } }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var original = TestMessages.Restore(value, new MessageId("port-message"), new TraceId("port-trace"),
            DateTimeOffset.Parse("2026-09-09T00:00:00Z"), new CorrelationId("port-exchange"),
            new MessageId("port-cause"), new Dictionary<string, FlowValue> { ["origin"] = "port-test" });
        var failure = new FlowError("port.failure", "Exact failure.", "test");
        var message = error ? original.WithValue(failure) : original;
        var occupied = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("occupied", new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        try
        {
            provider.GetKeyedService<bool>("main.node.Input").ShouldBe(signal);
            input.ShouldBeSameAs(target);
            output.ShouldBeSameAs(source);
            output.Completion.ShouldBeSameAs(source.Completion);

            target.Post(occupied).ShouldBeTrue();
            using var cancellation = new CancellationTokenSource();
            var waiting = input.SendAsync(message, cancellation.Token);
            waiting.IsCompleted.ShouldBeFalse();
            cancellation.Cancel();
            (await Should.ThrowAsync<OperationCanceledException>(() => waiting.WaitAsync(Deadline)))
                .CancellationToken.ShouldBe(cancellation.Token);
            (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(occupied);
            target.Count.ShouldBe(0);

            using var link = output.LinkTo(input, new DataflowLinkOptions { PropagateCompletion = false });
            (await source.SendAsync(message).WaitAsync(Deadline)).ShouldBeTrue();
            source.Complete();
            var received = await target.ReceiveAsync(Deadline);
            received.ShouldBeSameAs(message);
            received.Headers.ShouldBeSameAs(message.Headers);
            received.MessageId.ShouldBe(message.MessageId);
            received.TraceId.ShouldBe(message.TraceId);
            received.CorrelationId.ShouldBe(message.CorrelationId);
            received.CausationId.ShouldBe(message.CausationId);
            received.Timestamp.ShouldBe(message.Timestamp);
            received.IsError.ShouldBe(error);
            received.Value.ShouldBeSameAs(message.Value);
            received.Error.ShouldBeSameAs(message.Error);
            await output.Completion.WaitAsync(Deadline);
            target.Completion.IsCompleted.ShouldBeFalse();
            (await input.SendAsync(message).WaitAsync(Deadline)).ShouldBeTrue();
            (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        }
        finally
        {
            source.Complete();
            target.Complete();
            source.TryReceiveAll(out _);
            target.TryReceiveAll(out _);
            await Task.WhenAll(source.Completion, target.Completion).WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task Canonical_component_ports_reject_invalid_constructor_arguments()
    {
        var block = new BufferBlock<FlowMessage>();
        try
        {
            var services = new ServiceCollection();
            services.AddComponentFactory("main.node", _ => new PortNode(block, block));
            foreach (var name in new[] { null, "", " " })
            {
                Should.Throw<ArgumentException>(() => services.AddInputPort<PortNode>("main.node", name!, node => node.Input));
                Should.Throw<ArgumentException>(() => services.AddOutputPort<PortNode>("main.node", name!, node => node.Output));
            }
            Should.Throw<ArgumentNullException>(() => services.AddInputPort<PortNode>("main.node", "Input", null!));
            Should.Throw<ArgumentNullException>(() => services.AddOutputPort<PortNode>("main.node", "Output", null!));
            block.Completion.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            block.Complete();
            await block.Completion.WaitAsync(Deadline);
        }
    }

    [Fact]
    public void Canonical_component_port_contracts_expose_only_native_blocks()
    {
        foreach (var name in new[] { "ComponentInputPort", "ComponentOutputPort", "ComponentEventSource" })
            typeof(FlowApplication).Assembly.GetType("FluxFlow.Core.Application." + name).ShouldBeNull();
        typeof(FluxFlow.Core.Application.ComponentRegistrationExtensions).GetMethods()
            .Where(method => method.Name is "AddInputPort" or "AddOutputPort")
            .ShouldAllBe(method => method.GetParameters()[3].ParameterType.GetGenericArguments()[1].IsGenericType);
    }

    private sealed class PortNode(BufferBlock<FlowMessage> output, BufferBlock<FlowMessage> input) : IFlowNode
    {
        public ISourceBlock<FlowMessage> Output => output;
        public ITargetBlock<FlowMessage> Input => input;
        public Task Completion => Task.WhenAll(input.Completion, output.Completion);
        public void Complete() { input.Complete(); output.Complete(); }
        public void Fault(Exception error) { ((IDataflowBlock)input).Fault(error); ((IDataflowBlock)output).Fault(error); }
        public ValueTask DisposeAsync() { Complete(); return ValueTask.CompletedTask; }
    }

}
