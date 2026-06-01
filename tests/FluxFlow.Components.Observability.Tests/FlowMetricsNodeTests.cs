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

public sealed class FlowMetricsNodeTests
{
    [Fact]
    public async Task Metrics_TracksCountRateAndSize()
    {
        var runtimeNode = CreateNode(
            options => options
                .RegisterType<InputMessage>("message")
                .UseValueSelector<InputMessage>("payloadBytes", (message, _) => message.Payload.Length),
            new
            {
                inputType = "message",
                name = "messages",
                sizeSelector = "payloadBytes"
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var snapshots = new BufferBlock<FlowMetricSnapshot>();
        LinkSnapshots(runtimeNode, snapshots);

        await input.Target.SendAsync(new InputMessage("first", [1, 2], true));
        await Task.Delay(20);
        await input.Target.SendAsync(new InputMessage("second", [1, 2, 3, 4], true));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var first = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.Count.ShouldBe(1);
        first.LastSize.ShouldBe(2);
        first.TotalSize.ShouldBe(2);
        second.Count.ShouldBe(2);
        second.LastSize.ShouldBe(4);
        second.TotalSize.ShouldBe(6);
        second.AverageSize.ShouldBe(3);
        second.CurrentRatePerSecond.ShouldBeGreaterThan(0);
        second.AverageRatePerSecond.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Metrics_SizeSelectorFailureReportsErrorAndContinues()
    {
        var calls = 0;
        var runtimeNode = CreateNode(
            options => options
                .RegisterType<InputMessage>("message")
                .UseValueSelector<InputMessage>("payloadBytes", (message, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("size failed");
                    }

                    return message.Payload.Length;
                }),
            new
            {
                inputType = "message",
                sizeSelector = "payloadBytes"
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var errors = new BufferBlock<FlowError>();
        var snapshots = new BufferBlock<FlowMetricSnapshot>();
        runtimeNode.Node.Errors.LinkTo(errors);
        LinkSnapshots(runtimeNode, snapshots);

        await input.Target.SendAsync(new InputMessage("first", [1, 2], true));
        await input.Target.SendAsync(new InputMessage("second", [1, 2, 3], true));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ObservabilityErrorCodes.MetricsSizeSelectorFailed);
        var first = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await snapshots.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.Count.ShouldBe(1);
        first.LastSize.ShouldBeNull();
        second.Count.ShouldBe(2);
        second.LastSize.ShouldBe(3);
    }

    [Fact]
    public async Task Metrics_EmitsDiagnostics()
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
        diagnostic.Name.ShouldBe(ObservabilityDiagnosticNames.MetricsObserved);
        diagnostic.Attributes["name"].ShouldBe("items");
    }

    [Fact]
    public void Metrics_RejectsUnknownSizeSelector()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    sizeSelector = "missing"
                }));

        exception.Message.ShouldContain("missing");
    }

    private static RuntimeNode CreateNode(
        Action<ObservabilityComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterObservabilityComponents(configure);
        registry.TryGetFactory(ObservabilityComponentTypes.Metrics, out var factory).ShouldBeTrue();
        return factory(ObservabilityTestHost.CreateContext(
            ObservabilityComponentTypes.Metrics,
            configuration));
    }

    private static void LinkSnapshots(
        RuntimeNode runtimeNode,
        BufferBlock<FlowMetricSnapshot> target)
    {
        runtimeNode.FindOutput(new PortName(ObservabilityComponentPorts.Snapshots))!
            .TryLinkTo(
                new InputPort<FlowMetricSnapshot>(
                    new PortAddress("test", new NodeName("snapshots"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private sealed record InputMessage(string Kind, byte[] Payload, bool Enabled);
}
