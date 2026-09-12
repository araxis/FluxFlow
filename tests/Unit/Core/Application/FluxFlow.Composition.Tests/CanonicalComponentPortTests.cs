using System.Reflection;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
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
        bool factory, bool signal, bool error)
    {
        var source = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        var target = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        var kind = signal ? ComponentPortKind.Signal : ComponentPortKind.Message;
        var input = factory
            ? signal ? ComponentPorts.SignalInput(" Input ", target) : ComponentPorts.Input(" Input ", target)
            : new ComponentInputPort(" Input ", target, kind);
        var output = factory ? ComponentPorts.Output(" Output ", source) : new ComponentOutputPort(" Output ", source);
        var value = FlowValue.From(new { Text = "native-\u03bb", Values = new[] { 1, 2 }, Bytes = new byte[] { 0, 128, 255 } });
        var original = FlowMessage.Restore(value, new MessageId("port-message"), new TraceId("port-trace"),
            DateTimeOffset.Parse("2026-09-09T00:00:00Z"), new CorrelationId("port-exchange"),
            new MessageId("port-cause"), new Dictionary<string, string> { ["origin"] = "port-test" });
        var failure = new FlowError("port.failure", "Exact failure.", "test");
        var message = error ? original.WithError(failure) : original;
        var occupied = FlowMessage.Create(FlowValue.From("occupied"));

        try
        {
            input.Name.ShouldBe("Input");
            input.Kind.ShouldBe(kind);
            input.MessageType.ShouldBe(signal ? typeof(object) : typeof(FlowValue));
            input.Target.ShouldBeSameAs(target);
            output.Name.ShouldBe("Output");
            output.MessageType.ShouldBe(typeof(FlowValue));
            output.Source.ShouldBeSameAs(source);
            output.Source.Completion.ShouldBeSameAs(source.Completion);

            target.Post(occupied).ShouldBeTrue();
            using var cancellation = new CancellationTokenSource();
            var waiting = input.Target.SendAsync(message, cancellation.Token);
            waiting.IsCompleted.ShouldBeFalse();
            cancellation.Cancel();
            (await Should.ThrowAsync<OperationCanceledException>(() => waiting.WaitAsync(Deadline)))
                .CancellationToken.ShouldBe(cancellation.Token);
            (await target.ReceiveAsync(Deadline)).ShouldBeSameAs(occupied);
            target.Count.ShouldBe(0);

            using var link = output.Source.LinkTo(input.Target, new DataflowLinkOptions { PropagateCompletion = false });
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
            await output.Source.Completion.WaitAsync(Deadline);
            target.Completion.IsCompleted.ShouldBeFalse();
            (await input.Target.SendAsync(message).WaitAsync(Deadline)).ShouldBeTrue();
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
            foreach (var name in new[] { null, "", " " })
            {
                Should.Throw<ArgumentException>(() => new ComponentInputPort(name!, block)).ParamName.ShouldBe("name");
                Should.Throw<ArgumentException>(() => new ComponentOutputPort(name!, block)).ParamName.ShouldBe("name");
                Should.Throw<ArgumentException>(() => ComponentPorts.Input(name!, block)).ParamName.ShouldBe("name");
                Should.Throw<ArgumentException>(() => ComponentPorts.SignalInput(name!, block)).ParamName.ShouldBe("name");
                Should.Throw<ArgumentException>(() => ComponentPorts.Output(name!, block)).ParamName.ShouldBe("name");
            }
            Should.Throw<ArgumentNullException>(() => new ComponentInputPort("Input", null!)).ParamName.ShouldBe("target");
            Should.Throw<ArgumentNullException>(() => new ComponentOutputPort("Output", null!)).ParamName.ShouldBe("source");
            Should.Throw<ArgumentNullException>(() => ComponentPorts.Input("Input", null!)).ParamName.ShouldBe("target");
            Should.Throw<ArgumentNullException>(() => ComponentPorts.SignalInput("Input", null!)).ParamName.ShouldBe("target");
            Should.Throw<ArgumentNullException>(() => ComponentPorts.Output("Output", null!)).ParamName.ShouldBe("source");
            Should.Throw<ArgumentOutOfRangeException>(() => new ComponentInputPort("Input", block, (ComponentPortKind)42))
                .ParamName.ShouldBe("kind");
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
        typeof(ComponentInputPort).IsSealed.ShouldBeTrue();
        typeof(ComponentOutputPort).IsSealed.ShouldBeTrue();
        typeof(ComponentInputPort).GetConstructors().ShouldHaveSingleItem().GetParameters()
            .Select(parameter => parameter.ParameterType).ShouldBe([typeof(string), typeof(ITargetBlock<FlowMessage>), typeof(ComponentPortKind)]);
        typeof(ComponentOutputPort).GetConstructors().ShouldHaveSingleItem().GetParameters()
            .Select(parameter => parameter.ParameterType).ShouldBe([typeof(string), typeof(ISourceBlock<FlowMessage>)]);
        typeof(ComponentInputPort).GetProperty(nameof(ComponentInputPort.Target))!.PropertyType.ShouldBe(typeof(ITargetBlock<FlowMessage>));
        typeof(ComponentOutputPort).GetProperty(nameof(ComponentOutputPort.Source))!.PropertyType.ShouldBe(typeof(ISourceBlock<FlowMessage>));
        foreach (var type in new[] { typeof(RuntimeComponentBindingBuilder<>), typeof(RuntimeComponentInstanceBindingBuilder) })
        {
            var declarations = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            declarations.Length.ShouldBe(5);
            declarations.ShouldAllBe(method => !method.IsGenericMethod);
            declarations.Select(method => method.Name).Order(StringComparer.Ordinal)
                .ShouldBe(["HasEvents", "HasInput", "HasInput", "HasOutput", "HasSignalInput"]);
        }
    }
}
