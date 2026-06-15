using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class RuntimeNodeFactoryContextTests
{
    [Fact]
    public void GetResourceTyped_ReturnsResourceNodeAsHandle()
    {
        var handle = new HandleNode();
        var resource = RuntimeNode.Create(
            new NodeAddress(WellKnownScopes.Resources, new NodeName("broker")),
            handle);
        var context = CreateContext(("broker", resource));

        context.GetResource<IConnectionHandle>(new NodeName("broker"))
            .ShouldBeSameAs(handle);
    }

    [Fact]
    public void GetResourceTyped_Throws_WhenResourceMissing()
    {
        var context = CreateContext();

        Should.Throw<InvalidOperationException>(
            () => context.GetResource<IConnectionHandle>(new NodeName("broker")))
            .Message.ShouldContain("was not found");
    }

    [Fact]
    public void GetResourceTyped_Throws_WhenResourceDoesNotProvideType()
    {
        var resource = RuntimeNode.Create(
            new NodeAddress(WellKnownScopes.Resources, new NodeName("broker")),
            new PlainNode());
        var context = CreateContext(("broker", resource));

        Should.Throw<InvalidOperationException>(
            () => context.GetResource<IConnectionHandle>(new NodeName("broker")))
            .Message.ShouldContain("does not provide 'IConnectionHandle'");
    }

    private static RuntimeNodeFactoryContext CreateContext(
        params (string Name, RuntimeNode Node)[] resources)
        => new(
            new NodeName("op"),
            new NodeDefinition { Type = new NodeType("test.op") },
            "main",
            resources.ToDictionary(r => new NodeName(r.Name), r => r.Node));

    private interface IConnectionHandle
    {
    }

    private sealed class HandleNode : IFlowNode, IConnectionHandle
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();
        public Task Completion => _tcs.Task;
        public void Complete() => _tcs.TrySetResult();
        public void Fault(Exception exception) => _tcs.TrySetException(exception);
    }

    private sealed class PlainNode : IFlowNode
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();
        public Task Completion => _tcs.Task;
        public void Complete() => _tcs.TrySetResult();
        public void Fault(Exception exception) => _tcs.TrySetException(exception);
    }
}
