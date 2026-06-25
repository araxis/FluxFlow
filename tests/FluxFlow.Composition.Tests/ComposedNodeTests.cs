using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ComposedNodeTests
{
    [Fact]
    public async Task DisposeAsync_runs_cleanup_hook_when_node_dispose_fails()
    {
        var nodeException = new InvalidOperationException("node dispose failed");
        var node = new ThrowingDisposeNode(nodeException);
        var cleanupCalled = false;
        var descriptor = ComposedNode.Create(
            node,
            disposeAsync: () =>
            {
                cleanupCalled = true;
                return ValueTask.CompletedTask;
            });

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await descriptor.DisposeAsync());

        ReferenceEquals(exception, nodeException).ShouldBeTrue();
        cleanupCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsync_reports_node_and_cleanup_hook_failures()
    {
        var nodeException = new InvalidOperationException("node dispose failed");
        var hookException = new InvalidOperationException("cleanup hook failed");
        var descriptor = ComposedNode.Create(
            new ThrowingDisposeNode(nodeException),
            disposeAsync: () => throw hookException);

        var exception = await Should.ThrowAsync<AggregateException>(
            async () => await descriptor.DisposeAsync());

        exception.InnerExceptions.Count.ShouldBe(2);
        ReferenceEquals(exception.InnerExceptions[0], nodeException).ShouldBeTrue();
        ReferenceEquals(exception.InnerExceptions[1], hookException).ShouldBeTrue();
    }

    private sealed class ThrowingDisposeNode(Exception disposeException) : IFlowNode
    {
        public Task Completion { get; } = Task.CompletedTask;

        public void Complete()
        {
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
        }

        public ValueTask DisposeAsync() => throw disposeException;
    }
}
