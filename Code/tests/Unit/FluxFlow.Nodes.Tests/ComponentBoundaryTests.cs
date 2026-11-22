using FluxFlow.Core.Nodes;
using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;
using Xunit;
using Shouldly;

namespace FluxFlow.Nodes.Tests;

public sealed class ComponentBoundaryTests
{
    [Fact]
    public async Task Fault_waits_for_active_additional_input_before_releasing_component_resources()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new ActionBlock<FlowMessage>(_ => { });
        var additional = new ActionBlock<FlowMessage>(async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        });
        var output = new BroadcastBlock<FlowMessage>(static message => message);
        var lifetime = new FlowLifetime(this);
        lifetime.HasInput(input);
        lifetime.HasInput(additional);
        lifetime.HasOutput(output);
        lifetime.Observe();
        var failure = new InvalidOperationException("main input failed");
        try
        {
            await additional.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ((IDataflowBlock)input).Fault(failure);
            await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
            await Should.ThrowAsync<TimeoutException>(() => lifetime.Completion.WaitAsync(TimeSpan.FromMilliseconds(100)));
            release.TrySetResult();
            (await Should.ThrowAsync<InvalidOperationException>(() => lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5))))
                .ShouldBeSameAs(failure);
            additional.Completion.IsCompleted.ShouldBeTrue();
        }
        finally
        {
            release.TrySetResult();
            await lifetime.DisposeAsync();
        }
    }

    [Fact]
    public async Task Already_canceled_input_cannot_end_lifetime_before_other_blocks_are_declared()
    {
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        var input = new ActionBlock<FlowMessage>(_ => { }, new ExecutionDataflowBlockOptions { CancellationToken = stopping.Token });
        var output = new BroadcastBlock<FlowMessage>(static message => message);
        var events = new BroadcastBlock<FlowMessage>(static message => message);
        var side = new BroadcastBlock<FlowMessage>(static message => message);
        var reply = new ActionBlock<FlowMessage>(_ => { });
        var lifetime = new FlowLifetime(this, cancellationToken: stopping.Token);
        lifetime.HasInput(input);
        lifetime.HasOutput(output);
        lifetime.HasOutput(side);
        lifetime.HasEvents(events);
        lifetime.HasTerminalInput(reply);
        lifetime.Completion.IsCompleted.ShouldBeFalse();
        lifetime.Observe();
        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() => lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
            output.Completion.IsFaulted.ShouldBeTrue();
            side.Completion.IsFaulted.ShouldBeTrue();
            reply.Completion.IsFaulted.ShouldBeTrue();
            events.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally { await lifetime.DisposeAsync(); }
    }

    [Fact]
    public async Task Composed_lifetime_keeps_native_reply_input_available_while_processing_drains()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<FlowMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new BroadcastBlock<FlowMessage>(static message => message);
        var events = new BroadcastBlock<FlowMessage>(static message => message);
        var input = new ActionBlock<FlowMessage>(async _ =>
        {
            entered.TrySetResult();
            await output.SendAsync(await reply.Task);
        });
        var lifetime = new FlowLifetime(this);
        lifetime.HasInput(input);
        lifetime.HasOutput(output);
        lifetime.HasEvents(events);
        var replies = lifetime.HasTerminalInput(new ActionBlock<FlowMessage>(message => { reply.TrySetResult(message); }));
        lifetime.Observe();
        var received = new BufferBlock<FlowMessage>();
        using var link = output.LinkTo(received, new() { PropagateCompletion = true });
        var message = FlowMessage.Create((FlowValue)"reply");
        try
        {
            await input.SendAsync(message);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lifetime.Complete();
            lifetime.Completion.IsCompleted.ShouldBeFalse();
            (await replies.SendAsync(message)).ShouldBeTrue();
            (await received.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
            await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            replies.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally
        {
            reply.TrySetResult(message);
            await lifetime.DisposeAsync(() => ValueTask.CompletedTask);
        }
    }

    [Fact]
    public async Task Source_lifetime_rejects_new_blocks_after_start_and_completes_supplied_blocks()
    {
        var output = new BroadcastBlock<FlowMessage>(static message => message);
        var events = new BroadcastBlock<FlowMessage>(static message => message);
        var lifetime = new FlowLifetime(this, produce: token => Task.Delay(Timeout.InfiniteTimeSpan, token));
        lifetime.HasOutput(output);
        lifetime.HasEvents(events);
        var controls = lifetime.HasTerminalInput(new ActionBlock<FlowMessage>(_ => { }));
        lifetime.Observe();
        try
        {
            await lifetime.StartAsync();
            Should.Throw<InvalidOperationException>(() => lifetime.HasOutput(output));
            Should.Throw<InvalidOperationException>(() => lifetime.HasTerminalInput(controls));
            lifetime.Complete();
            await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            controls.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            output.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally { await lifetime.DisposeAsync(() => ValueTask.CompletedTask); }
    }

    [Fact]
    public async Task Lifetime_rejects_additional_outputs_after_completion_starts()
    {
        await using var node = new ComposedNode();
        var side = node.Side;
        node.Complete();
        Should.Throw<InvalidOperationException>(() => node.Coordinator.HasOutput(side));
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await side.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Reopening_observation_does_not_replace_or_orphan_running_blocks()
    {
        await using var node = new ComposedNode();
        Should.Throw<InvalidOperationException>(node.Coordinator.Observe);
        Should.Throw<InvalidOperationException>(() => node.Coordinator.HasInput(node.Input));

        var received = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(received, new() { PropagateCompletion = true });
        var message = FlowMessage.Create((FlowValue)"unchanged");
        (await node.Input.SendAsync(message)).ShouldBeTrue();
        node.Complete();
        (await received.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await received.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ComposedNode : FlowNode
    {
        public ComposedNode()
        {
            var input = new TransformBlock<FlowMessage, FlowMessage>(message => message);
            Input = input;
            Output = new BroadcastBlock<FlowMessage>(static message => message);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Side = new BroadcastBlock<FlowMessage>(static message => message);
            input.LinkTo(Output);
            Lifetime = new FlowLifetime(this);
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasOutput(Side);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }
        public FlowLifetime Coordinator => Lifetime;
        public ITargetBlock<FlowMessage> Input { get; }
        public BroadcastBlock<FlowMessage> Side { get; }
    }

    [Fact]
    public void Helper_base_does_not_impose_input_or_abstract_execution_methods()
    {
        typeof(FlowNode).GetProperty("Input").ShouldBeNull();
        typeof(FlowNode).GetMethods(System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .ShouldNotContain(method => method.IsAbstract);
        typeof(FlowNode).IsAssignableTo(typeof(IFlowSource)).ShouldBeFalse();
        typeof(FlowNode).GetMethods(System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .ShouldNotContain(method => method.Name == "CreateInput" || method.Name == "RunOnStart" ||
                method.Name == "AddInput" || method.Name == "AddOutput" || method.Name == "AddControlInput");
    }

    [Fact]
    public void Nodes_have_one_optional_authoring_base()
    {
        typeof(FlowNode).Assembly.GetExportedTypes()
            .Where(type => type.IsClass && type.IsAbstract && typeof(IFlowNode).IsAssignableFrom(type))
            .ShouldBe(new[] { typeof(FlowNode) });
    }

    [Theory]
    [InlineData(typeof(IFlowNode))]
    [InlineData(typeof(FlowNode))]
    [InlineData(typeof(IFlowSource))]
    public void Component_contracts_do_not_declare_application_registration(Type type)
        => type.GetMethods().ShouldNotContain(method => method.Name == "RegisterPorts");
}
