using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Processing_nodes_reject_invalid_output_capacity(int capacity)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new EmptyNode(capacity)).ParamName.ShouldBe("outputCapacity");
        Should.Throw<ArgumentOutOfRangeException>(() => new EmptyTransform(capacity)).ParamName.ShouldBe("outputCapacity");
    }

    private sealed class EmptyNode(int capacity) : FlowNode(outputCapacity: capacity)
    {
        protected override Task ProcessAsync(FlowMessage message) => Task.CompletedTask;
    }

    private sealed class EmptyTransform(int capacity) : FlowTransformNode(outputCapacity: capacity)
    {
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message) => Task.FromResult<IEnumerable<FlowMessage>>([message]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void Flow_source_rejects_invalid_output_capacity(int capacity)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new EmptySource(capacity));

        exception.ParamName.ShouldBe("outputCapacity");
    }

    private sealed class EmptySource(int capacity) : FlowSource(capacity)
    {
        protected override Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
