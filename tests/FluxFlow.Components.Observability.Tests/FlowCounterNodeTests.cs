using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

public sealed class FlowCounterNodeTests
{
    [Fact]
    public async Task Counter_CountsMatchingInputs()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                (_, context, _) => ((InputMessage)context.Variables["input"]!).Enabled))
                .RegisterType<InputMessage>("message"),
            new
            {
                inputType = "message",
                name = "accepted",
                predicate = "enabled"
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var snapshots = new BufferBlock<FlowCounterSnapshot>();
        LinkSnapshots(runtimeNode, snapshots);

        await input.Target.SendAsync(new InputMessage("first", [1], false));
        await input.Target.SendAsync(new InputMessage("second", [1], true));
        await input.Target.SendAsync(new InputMessage("third", [1], true));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var first = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.Count.ShouldBe(1);
        first.RejectedCount.ShouldBe(1);
        second.Count.ShouldBe(2);
        second.Name.ShouldBe("accepted");
        second.InputType.ShouldBe("message");
        snapshots.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Counter_ReportsPredicateFailureAndContinues()
    {
        var calls = 0;
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                (_, _, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("predicate failed");
                    }

                    return true;
                })),
            new
            {
                inputType = "int",
                predicate = "ok",
                expressionName = "counter-test"
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<int>>();
        var errors = new BufferBlock<FlowError>();
        var snapshots = new BufferBlock<FlowCounterSnapshot>();
        runtimeNode.Node.Errors.LinkTo(errors);
        LinkSnapshots(runtimeNode, snapshots);

        await input.Target.SendAsync(1);
        await input.Target.SendAsync(2);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ObservabilityErrorCodes.CounterPredicateFailed);
        error.Context!.ShouldContain("expressionName=counter-test");
        var snapshot = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Counter_WithoutPredicateDoesNotRequireExpressionEngine()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string"
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var snapshots = new BufferBlock<FlowCounterSnapshot>();
        LinkSnapshots(runtimeNode, snapshots);

        await input.Target.SendAsync("one");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshot = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Counter_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                name = "items"
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        runtimeNode.FindOutput(new PortName(ObservabilityComponentPorts.Snapshots))!
            .LinkToDiscard();

        await input.Target.SendAsync("hello");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(ObservabilityDiagnosticNames.CounterIncremented);
        diagnostic.Attributes["name"].ShouldBe("items");
    }

    private static RuntimeNode CreateNode(
        Action<ObservabilityComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterObservabilityComponents(configure);
        registry.TryGetFactory(ObservabilityComponentTypes.Counter, out var factory).ShouldBeTrue();
        return factory(ObservabilityTestHost.CreateContext(
            ObservabilityComponentTypes.Counter,
            configuration));
    }

    private static void LinkSnapshots(
        RuntimeNode runtimeNode,
        BufferBlock<FlowCounterSnapshot> target)
    {
        runtimeNode.FindOutput(new PortName(ObservabilityComponentPorts.Snapshots))!
            .TryLinkTo(
                new InputPort<FlowCounterSnapshot>(
                    new PortAddress("test", new NodeName("snapshots"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private sealed record InputMessage(string Kind, byte[] Payload, bool Enabled);
}
