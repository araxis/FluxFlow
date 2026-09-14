using System.Text.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationOutputDispatchTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Address =
        ApplicationAddress.WorkflowPort("Main", "Source", "Output");

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(1, true, true)]
    [InlineData(1, false, true)]
    public async Task Revision_fanout_joins_every_started_route_before_reporting_failure(
        int failureIndex, bool synchronous, bool cancellationCallbackThrows)
    {
        var failure = new InvalidOperationException("route failed");
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var offered = new ConcurrentQueue<(int Index, FlowMessage Message, bool Cancelled)>();
        var routing = new ApplicationRevisionRouting();
        var capture = new ControlledCapture();
        capture.Release.TrySetResult();
        var output = CreateOutput(capture, 2, routing: routing);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        var message = Messages(1)[0];
        SetRoutes(routing, Enumerable.Range(0, 3).Select(index => new DelegateRoute(index, (value, token) =>
        {
            offered.Enqueue((index, value, token.IsCancellationRequested));
            if (index == failureIndex)
                return synchronous ? throw failure : ValueTask.FromException(failure);
            return index == (failureIndex + 1) % 3 ? WaitForCancellationAsync(token) : ValueTask.CompletedTask;
        })).ToArray());

        try
        {
            (await source.Target.SendAsync(message)).ShouldBeTrue();
            await cleanupEntered.Task.WaitAsync(Deadline);
            offered.Select(item => item.Index).ShouldBe([0, 1, 2]);
            offered.ShouldAllBe(item => ReferenceEquals(item.Message, message) && !item.Cancelled);
            output.Completion.IsCompleted.ShouldBeFalse();
            receive.Task.IsCompleted.ShouldBeFalse();

            releaseCleanup.TrySetResult();
            (await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            (await source.Target.SendAsync(message)).ShouldBeFalse();
        }
        finally
        {
            releaseCleanup.TrySetResult();
            output.Abort();
            try { await output.Completion.WaitAsync(Deadline); } catch (InvalidOperationException) { }
        }

        async ValueTask WaitForCancellationAsync(CancellationToken token)
        {
            using var registration = token.Register(() =>
            {
                if (cancellationCallbackThrows) throw new InvalidOperationException("cancellation callback failed");
            });
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally
            {
                cleanupEntered.TrySetResult();
                await releaseCleanup.Task;
            }
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    public async Task Revision_fanout_keeps_captured_routes_until_delivery_settles(int routeCount, bool fallback)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldDeliveries = new ConcurrentQueue<FlowMessage>();
        var newDeliveries = new ConcurrentQueue<FlowMessage>();
        var activities = new ConcurrentQueue<ApplicationPortActivity>();
        var routing = new ApplicationRevisionRouting();
        var capture = new ControlledCapture();
        capture.Release.TrySetResult();
        var output = CreateOutput(capture, 2, activities.Enqueue, routing, discardUnmatched: routeCount == 0);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        var messages = Messages(2);
        SetRoutes(routing, Enumerable.Range(0, routeCount).Select(index => new DelegateRoute(index, async (message, token) =>
        {
            oldDeliveries.Enqueue(message);
            if (oldDeliveries.Count == routeCount) entered.TrySetResult();
            await release.Task.WaitAsync(token);
        }, isFallback: fallback)).ToArray());

        try
        {
            (await source.Target.SendAsync(messages[0])).ShouldBeTrue();
            if (routeCount != 0)
            {
                await entered.Task.WaitAsync(Deadline);
                receive.Task.IsCompleted.ShouldBeFalse();
            }
            else
                await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);

            // Swapping route selection must not wait for native target admission.
            await using (await output.BeginRevisionAsync(CancellationToken.None).AsTask().WaitAsync(Deadline))
            {
                SetRoutes(routing, [new DelegateRoute(10, (message, _) =>
                {
                    newDeliveries.Enqueue(message);
                    return ValueTask.CompletedTask;
                })]);
            }
            newDeliveries.ShouldBeEmpty();
            release.TrySetResult();
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            (await receive.Task.WaitAsync(Deadline)).Message.ShouldBeSameAs(messages[0]);
            oldDeliveries.Count.ShouldBe(routeCount);
            oldDeliveries.ShouldAllBe(message => ReferenceEquals(message, messages[0]));

            (await source.Target.SendAsync(messages[1])).ShouldBeTrue();
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            newDeliveries.Count.ShouldBe(1);
            newDeliveries.Single().ShouldBeSameAs(messages[1]);
            oldDeliveries.Count.ShouldBe(routeCount);
            activities.Select(activity => activity.MessageId).ShouldBe(messages.Select(message => message.MessageId));
            output.Complete();
            await output.Completion.WaitAsync(Deadline);
        }
        finally { release.TrySetResult(); output.Abort(); }
    }

    private static void SetRoutes(ApplicationRevisionRouting routing, IApplicationRevisionRoute[] routes)
        => routing.Swap(new ApplicationRevisionRouting.Snapshot(
            new Dictionary<ApplicationAddress, IReadOnlyList<IApplicationRevisionRoute>> { [Address] = routes },
            new HashSet<ApplicationRevisionRouting.RouteIdentity>()));

    private sealed class DelegateRoute(int index, Func<FlowMessage, CancellationToken, ValueTask> deliver,
        bool isFallback = false, Func<FlowMessage, bool>? matches = null)
        : IApplicationRevisionRoute
    {
        public ApplicationAddress Source => Address;
        public ApplicationAddress Target => ApplicationAddress.WorkflowPort("Main", $"Target{index}", "Input");
        public bool IsFallback => isFallback;
        public bool IsMatch(FlowMessage message) => matches?.Invoke(message) ?? true;
        public ValueTask DeliverAsync(FlowMessage message, CancellationToken cancellationToken)
            => deliver(message, cancellationToken);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(129)]
    public async Task Fallback_selection_evaluates_all_ordinary_routes_before_delivery(int routeCount)
    {
        var offered = new ConcurrentQueue<(int Index, FlowMessage Message)>();
        var evaluations = 0;
        var fallbackEvaluations = 0;
        var routing = new ApplicationRevisionRouting();
        var capture = new ControlledCapture();
        capture.Release.TrySetResult();
        var output = CreateOutput(capture, 2, routing: routing);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        var messages = Messages(2);
        var routes = Enumerable.Range(0, routeCount).Select(index => new DelegateRoute(index,
            (message, _) => { offered.Enqueue((index, message)); return ValueTask.CompletedTask; },
            matches: message => { evaluations++; return ReferenceEquals(message, messages[1]) && (index == 0 || index == routeCount - 1); }))
            .Cast<IApplicationRevisionRoute>().ToList();
        routes.Insert(0, new DelegateRoute(routeCount,
            (message, _) => { offered.Enqueue((routeCount, message)); return ValueTask.CompletedTask; },
            isFallback: true, matches: _ => { fallbackEvaluations++; return true; }));
        SetRoutes(routing, routes.ToArray());
        try
        {
            foreach (var message in messages)
            {
                (await source.Target.SendAsync(message)).ShouldBeTrue();
                await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            }
            evaluations.ShouldBe(routeCount * 2);
            fallbackEvaluations.ShouldBe(1);
            offered.Select(item => item.Index).ShouldBe([routeCount, 0, routeCount - 1]);
            offered.First().Message.ShouldBeSameAs(messages[0]);
            offered.Skip(1).ShouldAllBe(item => ReferenceEquals(item.Message, messages[1]));
            output.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { output.Abort(); await output.Completion.WaitAsync(Deadline); }
    }

    [Fact]
    public async Task Fallback_selection_rejects_ambiguous_snapshots_before_delivery()
    {
        var offered = 0;
        var routing = new ApplicationRevisionRouting();
        var capture = new ControlledCapture();
        capture.Release.TrySetResult();
        var output = CreateOutput(capture, 2, routing: routing);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        SetRoutes(routing, Enumerable.Range(0, 3).Select(index => new DelegateRoute(index,
            (_, _) => { offered++; return ValueTask.CompletedTask; }, isFallback: index != 0)).ToArray());
        try
        {
            (await source.Target.SendAsync(Messages(1)[0])).ShouldBeTrue();
            (await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(Deadline)))
                .Message.ShouldContain("only one fallback");
            offered.ShouldBe(0);
            (await source.Target.SendAsync(Messages(1)[0])).ShouldBeFalse();
        }
        finally
        {
            output.Abort();
            try { await output.Completion.WaitAsync(Deadline); } catch (InvalidOperationException) { }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Output_capacity_includes_active_dispatch(int capacity)
    {
        var capture = new ControlledCapture();
        var output = CreateOutput(capture, capacity, discardUnmatched: true);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        using var cancellation = new CancellationTokenSource();
        var messages = Messages(capacity);
        try
        {
            (await source.Target.SendAsync(messages[0])).ShouldBeTrue();
            await capture.Entered.Task.WaitAsync(Deadline);
            foreach (var message in messages.Skip(1))
                (await source.Target.SendAsync(message).WaitAsync(Deadline)).ShouldBeTrue();

            var extra = source.Target.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("extra", new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellation.Token);
            extra.IsCompleted.ShouldBeFalse();
            capture.Messages.Count.ShouldBe(1);
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => extra.WaitAsync(Deadline));

            capture.Release.TrySetResult();
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            AssertCaptured(capture, messages);
            output.Complete();
            await output.Completion.WaitAsync(Deadline);
            (await source.Target.SendAsync(messages[0])).ShouldBeFalse();
        }
        finally { capture.Release.TrySetResult(); output.Abort(); }
    }

    [Fact]
    public async Task Drain_barrier_waits_for_revision_and_preserves_dispatch_order()
    {
        var capture = new ControlledCapture();
        capture.Release.TrySetResult();
        var activities = new ConcurrentQueue<ApplicationPortActivity>();
        var output = CreateOutput(capture, 4, activities.Enqueue, discardUnmatched: true);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        await using var revision = await output.BeginRevisionAsync(CancellationToken.None);
        var messages = Messages(3);
        try
        {
            foreach (var message in messages.Take(2))
                (await source.Target.SendAsync(message).WaitAsync(Deadline)).ShouldBeTrue();
            var drain = output.DrainAsync(CancellationToken.None).AsTask();
            drain.IsCompleted.ShouldBeFalse();
            capture.Messages.ShouldBeEmpty();
            receive.Task.IsCompleted.ShouldBeFalse();

            await revision.DisposeAsync();
            await drain.WaitAsync(Deadline);
            AssertCaptured(capture, messages.Take(2).ToArray());
            (await receive.Task.WaitAsync(Deadline)).Message.ShouldBeSameAs(messages[0]);

            (await source.Target.SendAsync(messages[2])).ShouldBeTrue();
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            AssertCaptured(capture, messages);
            activities.Select(activity => activity.MessageId).ShouldBe(messages.Select(message => message.MessageId));
            output.Complete();
            await output.Completion.WaitAsync(Deadline);
        }
        finally { output.Abort(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Drain_cancellation_does_not_cancel_accepted_messages(bool full)
    {
        var capture = new ControlledCapture();
        var output = CreateOutput(capture, 2, discardUnmatched: true);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        using var cancellation = new CancellationTokenSource();
        var messages = Messages(full ? 2 : 1);
        try
        {
            (await source.Target.SendAsync(messages[0])).ShouldBeTrue();
            await capture.Entered.Task.WaitAsync(Deadline);
            if (full) (await source.Target.SendAsync(messages[1])).ShouldBeTrue();
            var drain = output.DrainAsync(cancellation.Token).AsTask();
            drain.IsCompleted.ShouldBeFalse();
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => drain.WaitAsync(Deadline));
            capture.Messages.Count.ShouldBe(1);
            output.Completion.IsCompleted.ShouldBeFalse();

            capture.Release.TrySetResult();
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            AssertCaptured(capture, messages);
            output.Complete();
            await output.Completion.WaitAsync(Deadline);
        }
        finally { capture.Release.TrySetResult(); output.Abort(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_failure_releases_pending_drains_with_original_failure(bool full)
    {
        var failure = new InvalidOperationException("capture failed");
        var capture = new ControlledCapture(failure);
        var activities = new ConcurrentQueue<ApplicationPortActivity>();
        var output = CreateOutput(capture, 2, activities.Enqueue);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        var messages = Messages(2);
        try
        {
            (await source.Target.SendAsync(messages[0])).ShouldBeTrue();
            await capture.Entered.Task.WaitAsync(Deadline);
            if (full) (await source.Target.SendAsync(messages[1])).ShouldBeTrue();
            var drain = output.DrainAsync(CancellationToken.None).AsTask();
            drain.IsCompleted.ShouldBeFalse();
            capture.Release.TrySetResult();

            (await Should.ThrowAsync<InvalidOperationException>(() => drain.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            (await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            (await Should.ThrowAsync<InvalidOperationException>(() =>
                output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
            (await source.Target.SendAsync(messages[0])).ShouldBeFalse();
            AssertCaptured(capture, [messages[0]]);
            activities.ShouldBeEmpty();
        }
        finally { capture.Release.TrySetResult(); output.Abort(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Abort_releases_drains_while_revision_is_held(bool full)
    {
        var capture = new ControlledCapture();
        var output = CreateOutput(capture, 2);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        await using var revision = await output.BeginRevisionAsync(CancellationToken.None);
        try
        {
            (await source.Target.SendAsync(Messages(1)[0])).ShouldBeTrue();
            if (full) (await source.Target.SendAsync(Messages(1)[0])).ShouldBeTrue();
            var drain = output.DrainAsync(CancellationToken.None).AsTask();
            drain.IsCompleted.ShouldBeFalse();
            output.Abort();
            await drain.WaitAsync(Deadline);
            await output.Completion.WaitAsync(Deadline);
            capture.Messages.ShouldBeEmpty();
            output.GetStatus().PendingMessages.ShouldBe(0);
        }
        finally { output.Abort(); }
    }

    [Fact]
    public async Task Detached_source_keeps_in_flight_output_available()
    {
        var capture = new ControlledCapture();
        var output = CreateOutput(capture, 2, discardUnmatched: true);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        var message = Messages(1)[0];
        try
        {
            (await source.Target.SendAsync(message)).ShouldBeTrue();
            await capture.Entered.Task.WaitAsync(Deadline);
            attachment.Dispose();
            output.GetStatus().ActiveAttachments.ShouldBe(0);
            output.GetStatus().PendingMessages.ShouldBe(1);
            using var receive = output.RegisterReceive();
            receive.Task.IsCompleted.ShouldBeFalse();
            var observed = output.Observe<FlowValue>(2);
            observed.Status.ShouldBe(PortObserveStatus.Started);
            await using var observation = observed.Observation.ShouldNotBeNull();

            capture.Release.TrySetResult();
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            (await receive.Task.WaitAsync(Deadline)).Message.ShouldBeSameAs(message);
            var actual = await observation.Messages.ReceiveAsync().WaitAsync(Deadline);
            actual.MessageId.ShouldBe(message.MessageId);
            actual.Value.ShouldBeSameAs(message.Value);
            output.GetStatus().PendingMessages.ShouldBe(0);
            using var unavailable = output.RegisterReceive();
            (await unavailable.Task.WaitAsync(Deadline)).Status.ShouldBe(PortReceiveStatus.Unavailable);
            output.Complete();
            await output.Completion.WaitAsync(Deadline);
        }
        finally { capture.Release.TrySetResult(); output.Abort(); }
    }

    [Fact]
    public async Task Abort_prevents_late_capture_from_dispatching()
    {
        var capture = new ControlledCapture(ignoreCancellation: true);
        var activities = new ConcurrentQueue<ApplicationPortActivity>();
        var output = CreateOutput(capture, 2, activities.Enqueue);
        var source = new LinkedSource();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        await using var observation = output.Observe<FlowValue>(2).Observation.ShouldNotBeNull();
        var message = Messages(1)[0];
        try
        {
            (await source.Target.SendAsync(message)).ShouldBeTrue();
            await capture.Entered.Task.WaitAsync(Deadline);
            output.Abort();
            await output.Completion.WaitAsync(Deadline);
            source.Target.Completion.IsCompleted.ShouldBeFalse();
            capture.Release.TrySetResult();
            await Should.ThrowAsync<OperationCanceledException>(() => source.Target.Completion.WaitAsync(Deadline));

            AssertCaptured(capture, [message]);
            activities.ShouldBeEmpty();
            (await receive.Task.WaitAsync(Deadline)).Status.ShouldBe(PortReceiveStatus.Completed);
            ((IReceivableSourceBlock<FlowMessage<FlowValue>>)observation.Messages)
                .TryReceive(out _).ShouldBeFalse();
        }
        finally { capture.Release.TrySetResult(); output.Abort(); }
    }

    private static ApplicationOutputPort CreateOutput(ControlledCapture capture, int capacity,
        Action<ApplicationPortActivity>? activity = null, ApplicationRevisionRouting? routing = null,
        bool discardUnmatched = false)
        => new(Address, capacity, static _ => { }, activity ?? (static _ => { }),
            routing ?? new ApplicationRevisionRouting(), capture, discardUnmatched: discardUnmatched);

    private static FlowMessage[] Messages(int count)
        => Enumerable.Range(0, count).Select(index => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(index, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();

    private static void AssertCaptured(ControlledCapture capture, FlowMessage[] expected)
    {
        var actual = capture.Messages.ToArray();
        actual.Length.ShouldBe(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            actual[index].MessageId.ShouldBe(expected[index].MessageId);
            actual[index].Value.ShouldBeSameAs(expected[index].Value);
            actual[index].Headers.ShouldBeSameAs(expected[index].Headers);
        }
    }

    private sealed class ControlledCapture(Exception? failure = null, bool ignoreCancellation = false)
        : IApplicationOutputCapture
    {
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask CaptureAsync(FlowMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Enqueue(message);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ignoreCancellation ? CancellationToken.None : cancellationToken);
            if (failure is not null) throw failure;
        }
    }

    // Captures the real target supplied to a native source, without another test-side queue
    // between SendAsync and the output port's admission boundary.
    private sealed class LinkedSource : ISourceBlock<FlowMessage>
    {
        private readonly BufferBlock<FlowMessage> _source = new();
        public ITargetBlock<FlowMessage> Target { get; private set; } = null!;
        public Task Completion => _source.Completion;
        public IDisposable LinkTo(ITargetBlock<FlowMessage> target, DataflowLinkOptions options)
        {
            Target = target;
            return _source.LinkTo(target, options);
        }
        public FlowMessage ConsumeMessage(DataflowMessageHeader header, ITargetBlock<FlowMessage> target, out bool consumed)
            => ((ISourceBlock<FlowMessage>)_source).ConsumeMessage(header, target, out consumed)!;
        public bool ReserveMessage(DataflowMessageHeader header, ITargetBlock<FlowMessage> target)
            => ((ISourceBlock<FlowMessage>)_source).ReserveMessage(header, target);
        public void ReleaseReservation(DataflowMessageHeader header, ITargetBlock<FlowMessage> target)
            => ((ISourceBlock<FlowMessage>)_source).ReleaseReservation(header, target);
        public void Complete() => _source.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_source).Fault(error);
    }
}
