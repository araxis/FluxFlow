using FluxFlow.Components.Metrics.Contracts;
using FluxFlow.Components.Metrics.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Metrics.Tests;

public sealed class MetricsAggregateNodeTests
{
    [Fact]
    public async Task Aggregate_TracksCountValueAndSize()
    {
        var runtimeNode = CreateNode(new
        {
            rateWindowSeconds = 10
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        await input.Target.SendAsync(new MetricSampleInput
        {
            Timestamp = start,
            Name = "items",
            Value = 2,
            Size = 10
        });
        await input.Target.SendAsync(new MetricSampleInput
        {
            Timestamp = start.AddSeconds(1),
            Name = "items",
            Value = 4,
            Size = 20
        });
        input.Target.Complete();

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.SampleCount.ShouldBe(1);
        first.TotalValue.ShouldBe(2);
        second.SampleCount.ShouldBe(2);
        second.ValueCount.ShouldBe(2);
        second.TotalValue.ShouldBe(6);
        second.AverageValue.ShouldBe(3);
        second.MinValue.ShouldBe(2);
        second.MaxValue.ShouldBe(4);
        second.TotalSize.ShouldBe(30);
    }

    [Fact]
    public async Task Aggregate_CalculatesRatesFromSampleTimestamps()
    {
        var runtimeNode = CreateNode(new
        {
            rateWindowSeconds = 2
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        await input.Target.SendAsync(new MetricSampleInput { Timestamp = start });
        await input.Target.SendAsync(new MetricSampleInput { Timestamp = start.AddSeconds(1) });
        await input.Target.SendAsync(new MetricSampleInput { Timestamp = start.AddSeconds(3) });
        input.Target.Complete();

        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var third = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        third.SampleCount.ShouldBe(3);
        third.CurrentRate.ShouldBe(1d);
        third.AverageRate.ShouldBe(1d);
    }

    [Fact]
    public async Task Aggregate_GroupsByTag()
    {
        var runtimeNode = CreateNode(new
        {
            groupByTag = "topic",
            rateWindowSeconds = 10
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        await input.Target.SendAsync(new MetricSampleInput
        {
            Timestamp = start,
            Group = "ignored",
            Size = 3,
            Tags = new Dictionary<string, string> { ["topic"] = "sensors/a" }
        });
        await input.Target.SendAsync(new MetricSampleInput
        {
            Timestamp = start.AddSeconds(1),
            Group = "ignored",
            Size = 4,
            Tags = new Dictionary<string, string> { ["topic"] = "sensors/b" }
        });
        input.Target.Complete();

        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        second.Groups.Keys.ShouldBe(["sensors/a", "sensors/b"], ignoreOrder: true);
        second.Groups["sensors/a"].Count.ShouldBe(1);
        second.Groups["sensors/a"].TotalSize.ShouldBe(3);
        second.Groups["sensors/b"].Count.ShouldBe(1);
        second.Groups["sensors/b"].TotalSize.ShouldBe(4);
    }

    [Fact]
    public async Task Aggregate_TrimsInactiveGroupRatesFromSnapshotTimestamp()
    {
        var runtimeNode = CreateNode(new
        {
            rateWindowSeconds = 2
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        await input.Target.SendAsync(new MetricSampleInput
        {
            Timestamp = start,
            Group = "a"
        });
        await input.Target.SendAsync(new MetricSampleInput
        {
            Timestamp = start.AddSeconds(3),
            Group = "b"
        });
        input.Target.Complete();

        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        second.Groups["a"].CurrentRate.ShouldBe(0);
        second.Groups["b"].CurrentRate.ShouldBe(0.5);
        second.CurrentRate.ShouldBe(0.5);
    }

    [Fact]
    public async Task Aggregate_HandlesNullTagsAsDefaultGroup()
    {
        var runtimeNode = CreateNode(new
        {
            groupByTag = "topic"
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);

        await input.Target.SendAsync(new MetricSampleInput
        {
            Tags = null!
        });
        input.Target.Complete();

        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.Groups.Keys.ShouldBe(["default"]);
    }

    [Fact]
    public async Task Aggregate_EmitsOnlyFinalSnapshotWhenConfigured()
    {
        var runtimeNode = CreateNode(new
        {
            emitEverySample = false
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);

        await input.Target.SendAsync(new MetricSampleInput { Value = 1 });
        await input.Target.SendAsync(new MetricSampleInput { Value = 2 });
        input.Target.Complete();

        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.SampleCount.ShouldBe(2);
        output.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Aggregate_MissingValueCountsAsEventWithoutNumericZero()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);

        await input.Target.SendAsync(new MetricSampleInput());
        input.Target.Complete();
        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.SampleCount.ShouldBe(1);
        snapshot.ValueCount.ShouldBe(0);
        snapshot.TotalValue.ShouldBeNull();
        snapshot.AverageValue.ShouldBeNull();
    }

    [Fact]
    public async Task Aggregate_CanTreatMissingValueAsZero()
    {
        var runtimeNode = CreateNode(new
        {
            treatMissingValueAsZero = true
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);

        await input.Target.SendAsync(new MetricSampleInput());
        input.Target.Complete();
        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.SampleCount.ShouldBe(1);
        snapshot.ValueCount.ShouldBe(1);
        snapshot.TotalValue.ShouldBe(0);
        snapshot.AverageValue.ShouldBe(0);
    }

    [Fact]
    public async Task Aggregate_RespectsMaxGroupLimit()
    {
        var runtimeNode = CreateNode(new
        {
            maxGroups = 1
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);
        var errors = LinkOutput<FlowError>(
            runtimeNode,
            MetricsComponentPorts.Errors);

        await input.Target.SendAsync(new MetricSampleInput { Group = "a" });
        await input.Target.SendAsync(new MetricSampleInput { Group = "b" });
        input.Target.Complete();

        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        second.SampleCount.ShouldBe(2);
        second.Groups.Keys.ShouldBe(["a"]);
        error.Code.ShouldBe(MetricsErrorCodes.GroupLimitReached);
    }

    [Fact]
    public async Task Aggregate_ReportsInvalidSizeAndContinues()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);
        var errors = LinkOutput<FlowError>(
            runtimeNode,
            MetricsComponentPorts.Errors);

        await input.Target.SendAsync(new MetricSampleInput { Size = -1 });
        await input.Target.SendAsync(new MetricSampleInput { Size = 3 });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var snapshot = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(MetricsErrorCodes.InvalidSample);
        snapshot.SampleCount.ShouldBe(1);
        snapshot.TotalSize.ShouldBe(3);
    }

    [Fact]
    public async Task Aggregate_CompletesCleanlyWithUnlinkedOutput()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);

        await input.Target.SendAsync(new MetricSampleInput
        {
            Value = 1,
            Size = 10
        });
        input.Target.Complete();

        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Aggregate_DoesNotBlockWhenUnlinkedOutputIsFull()
    {
        var runtimeNode = CreateNode(new
        {
            boundedCapacity = 1
        });
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<FlowError>(
            runtimeNode,
            MetricsComponentPorts.Errors);

        await input.Target.SendAsync(new MetricSampleInput { Value = 1 });
        await input.Target.SendAsync(new MetricSampleInput { Value = 2 });
        await input.Target.SendAsync(new MetricSampleInput { Value = 3 });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MetricsErrorCodes.SnapshotDropped);
    }

    [Fact]
    public async Task Aggregate_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<MetricSnapshotOutput>(runtimeNode);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);

        await input.Target.SendAsync(new MetricSampleInput
        {
            Value = 1,
            Group = "items"
        });
        input.Target.Complete();
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(MetricsDiagnosticNames.AggregateUpdated);
        diagnostic.Attributes["sampleCount"].ShouldBe(1L);
    }

    [Fact]
    public void Aggregate_RejectsInvalidOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void RegisterMetricsComponents_RegistersAggregateNode()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMetricsComponents();

        registry.TryGetFactory(MetricsComponentTypes.Aggregate, out _).ShouldBeTrue();
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMetricsComponents();
        registry.TryGetFactory(MetricsComponentTypes.Aggregate, out var factory).ShouldBeTrue();
        return factory(CreateContext(configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("metrics"),
            new NodeDefinition
            {
                Type = MetricsComponentTypes.Aggregate,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
    }

    private static InputPort<MetricSampleInput> GetInput(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(MetricsComponentPorts.Input))
            .ShouldBeOfType<InputPort<MetricSampleInput>>();

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName = MetricsComponentPorts.Output)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }
}
