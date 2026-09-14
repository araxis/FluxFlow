using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed class PreparedOutputDispatchTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Prepared_routes_share_native_dispatch_and_drain()
    {
        var preparing = Signal();
        var captured = Signal();
        var delivering = Signal();
        var admitted = Signal();
        var firstMessages = new ConcurrentQueue<FlowMessage>();
        var nextMessages = new ConcurrentQueue<FlowMessage>();
        var notifications = new ConcurrentQueue<FlowMessage>();
        var preparationCount = 0;
        IFlowOutputRoute<FlowMessage>[] routes = [new Route(async (message, token) =>
        {
            firstMessages.Enqueue(message);
            delivering.TrySetResult();
            await admitted.Task.WaitAsync(token);
        })];
        await using var output = new FlowOutput<FlowMessage>(new() { Capacity = 2 }, capacityIncludesActive: true);
        output.ConfigureDispatch(async (message, token) =>
        {
            Interlocked.Increment(ref preparationCount);
            preparing.TrySetResult();
            await captured.Task.WaitAsync(token);
            return new(routes, () => notifications.Enqueue(message));
        });
        var ingress = (ITargetBlock<FlowMessage>)output.Ingress;
        var messages = Enumerable.Range(1, 3).Select(value => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        try
        {
            ingress.Post(messages[0]).ShouldBeTrue();
            await preparing.Task.WaitAsync(Deadline);
            firstMessages.ShouldBeEmpty();
            notifications.ShouldBeEmpty();
            ingress.Post(messages[1]).ShouldBeTrue();
            var third = ingress.SendAsync(messages[2]);
            var drain = output.DrainAsync(CancellationToken.None).AsTask();
            third.IsCompleted.ShouldBeFalse();
            drain.IsCompleted.ShouldBeFalse();
            output.PendingPreparedMessages.ShouldBe(2);

            captured.TrySetResult();
            await delivering.Task.WaitAsync(Deadline);
            routes = [new Route((message, _) =>
            {
                nextMessages.Enqueue(message);
                return ValueTask.CompletedTask;
            })];
            firstMessages.Single().ShouldBeSameAs(messages[0]);
            nextMessages.ShouldBeEmpty();
            notifications.ShouldBeEmpty();
            preparationCount.ShouldBe(1);
            third.IsCompleted.ShouldBeFalse();
            drain.IsCompleted.ShouldBeFalse();

            admitted.TrySetResult();
            (await third.WaitAsync(Deadline)).ShouldBeTrue();
            await drain.WaitAsync(Deadline);
            output.Complete();
            await output.Completion.WaitAsync(Deadline);
            output.PendingPreparedMessages.ShouldBe(0);
            firstMessages.Count.ShouldBe(1);
            nextMessages.ToArray().ShouldBe(messages.Skip(1).ToArray());
            notifications.ToArray().ShouldBe(messages);
            for (var index = 0; index < messages.Length; index++)
                notifications.ToArray()[index].ShouldBeSameAs(messages[index]);
            preparationCount.ShouldBe(3);
        }
        finally
        {
            captured.TrySetResult();
            admitted.TrySetResult();
        }
    }

    [Fact]
    public async Task Prepared_route_failure_faults_drain_and_cancels_siblings()
    {
        var canceled = Signal();
        var entered = Signal();
        var fail = Signal();
        var failure = new InvalidOperationException("target failed");
        var siblingMessages = new ConcurrentQueue<FlowMessage>();
        var notified = false;
        var output = new FlowOutput<FlowMessage>(new() { Capacity = 2 }, capacityIncludesActive: true);
        output.ConfigureDispatch((message, _) => ValueTask.FromResult(new FlowOutputDispatch<FlowMessage>(
        [
            new Route(async (_, token) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { canceled.TrySetResult(); }
            }),
            new Route(async (_, token) => { await fail.Task.WaitAsync(token); throw failure; }),
            new Route((value, _) => { siblingMessages.Enqueue(value); return ValueTask.CompletedTask; })
        ], () => notified = true)));
        var value = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(7, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        ((ITargetBlock<FlowMessage>)output.Ingress).Post(value).ShouldBeTrue();
        await entered.Task.WaitAsync(Deadline);
        var drain = output.DrainAsync(CancellationToken.None).AsTask();
        drain.IsCompleted.ShouldBeFalse();
        fail.TrySetResult();
        (await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => drain.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        await canceled.Task.WaitAsync(Deadline);
        siblingMessages.Single().ShouldBeSameAs(value);
        notified.ShouldBeFalse();
        output.PendingPreparedMessages.ShouldBe(0);
        await output.DisposeAsync();
    }

    [Fact]
    public async Task Preparation_failure_never_delivers_and_faults_drain()
    {
        var entered = Signal();
        var fail = Signal();
        var failure = new InvalidOperationException("preparation failed");
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 2 });
        var output = new FlowOutput<FlowMessage>(new() { Capacity = 2 }, capacityIncludesActive: true);
        using var nativeLink = output.LinkTo(target, new DataflowLinkOptions());
        output.ConfigureDispatch(async (_, token) =>
        {
            entered.TrySetResult();
            await fail.Task.WaitAsync(token);
            throw failure;
        });
        ((ITargetBlock<FlowMessage>)output.Ingress).Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeTrue();
        await entered.Task.WaitAsync(Deadline);
        var drain = output.DrainAsync(CancellationToken.None).AsTask();
        drain.IsCompleted.ShouldBeFalse();
        fail.TrySetResult();
        (await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() => drain.WaitAsync(Deadline))).ShouldBeSameAs(failure);
        target.TryReceive(out _).ShouldBeFalse();
        target.Complete();
        await target.Completion.WaitAsync(Deadline);
        output.PendingPreparedMessages.ShouldBe(0);
        await output.DisposeAsync();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Route(Func<FlowMessage, CancellationToken, ValueTask> send) : IFlowOutputRoute<FlowMessage>
    {
        public ValueTask DeliverAsync(FlowMessage value, CancellationToken cancellationToken) => send(value, cancellationToken);
    }
}
