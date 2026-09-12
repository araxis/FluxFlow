using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Mapping;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationPortRuntimeTests
{
    private static readonly TimeSpan DeliveryDeadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosted_signal_delivery_waits_for_capacity_and_honors_unlink(bool unlink)
    {
        var link = CompileLinks("\"Sink.Input\"", ["Sink"]).Single();
        var emitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var output = new ApplicationOutputPort(Output, 2, rejections.Enqueue, _ => emitted.TrySetResult(), new());
        var input = new ApplicationInputPort(link.Target, 2, rejections.Enqueue, static _ => { }, ApplicationPortKind.Signal);
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 2 });
        await using var inputAttachment = await input.AttachAsync(target, CancellationToken.None);
        using var route = output.Connect(input, link);
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = output.Attach(source);
        var retained = Enumerable.Range(0, 4).Select(i => Message($"retained-{i}")).ToArray();
        var routed = Message("signal");
        try
        {
            foreach (var message in retained.Take(2))
                (await target.SendAsync(message)).ShouldBeTrue();
            foreach (var message in retained.Skip(2))
                (await input.SendAsync(message, null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            source.Post(routed).ShouldBeTrue();
            await emitted.Task.WaitAsync(DeliveryDeadline);
            var drained = output.DrainAsync(CancellationToken.None).AsTask();
            drained.IsCompleted.ShouldBeFalse();
            if (unlink) route.Dispose();
            foreach (var expected in retained)
                (await target.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(expected);
            await drained.WaitAsync(DeliveryDeadline);
            if (!unlink)
            {
                var delivered = (await target.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeOfType<FlowMessage>();
                delivered.ShouldBeSameAs(routed);
                delivered.Value.ShouldBeSameAs(routed.Value);
                delivered.MessageId.ShouldBe(routed.MessageId);
                delivered.TraceId.ShouldBe(routed.TraceId);
            }
            target.TryReceive(out _).ShouldBeFalse();
            rejections.ShouldBeEmpty();
        }
        finally { output.Abort(); input.Abort(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Hosted_revision_under_saturation_preserves_selected_routes_and_cancellation(bool cancel, bool signal)
    {
        var links = CompileLinks("[\"Slow.Input\",\"Healthy.Input\"]", ["Slow", "Healthy"]);
        var slowLink = links.Single(link => link.Target.Value.Contains("Slow", StringComparison.Ordinal));
        var healthyLink = links.Single(link => link.Target != slowLink.Target);
        var runtimeBuilder = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output, capacity: 2).AddInput(healthyLink.Target, capacity: 2);
        if (signal) runtimeBuilder.AddSignalInput(slowLink.Target, capacity: 2);
        else runtimeBuilder.AddInput(slowLink.Target, capacity: 2);
        await using var runtime = runtimeBuilder.Build();
        var oldTarget = new PostponedTarget<FlowMessage>();
        var replacement = new BufferBlock<FlowMessage>();
        var healthy = new BufferBlock<FlowMessage>();
        await using var oldAttachment = signal ? await runtime.AttachSignalInputAsync(slowLink.Target, oldTarget)
            : await runtime.AttachInputAsync(slowLink.Target, oldTarget);
        await using var healthyAttachment = await runtime.AttachInputAsync(healthyLink.Target, healthy);
        var source = new BufferBlock<FlowMessage>();
        await using var firstBuilder = runtime.CreateRevision("first").AttachOutput(Output, source).SetLinks(links);
        await using var firstRevision = firstBuilder.Build();
        await using var firstLease = await firstRevision.ActivateAsync();
        var claimed = Message("claimed");
        var queued = Message("queued");
        var selected = Message("selected-before-revision");
        try
        {
            (await runtime.SendAsync(slowLink.Target, claimed)).IsAccepted.ShouldBeTrue();
            await oldTarget.NextOfferAsync();
            (await runtime.SendAsync(slowLink.Target, queued)).IsAccepted.ShouldBeTrue();
            var selectedDispatch = runtime.ReceiveAsync<FlowValue>(Output, DeliveryDeadline);
            source.Post(selected).ShouldBeTrue();
            (await healthy.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(selected);
            await using var nextBuilder = runtime.CreateRevision("next").SetLinks([healthyLink]);
            if (signal) nextBuilder.ReplaceSignalInput(slowLink.Target, replacement);
            else nextBuilder.ReplaceInput(slowLink.Target, replacement);
            await using var nextRevision = nextBuilder.Build();
            using var cancellation = new CancellationTokenSource();
            var activation = nextRevision.ActivateAsync(cancellation.Token).AsTask();
            activation.IsCompleted.ShouldBeFalse();
            if (cancel)
            {
                cancellation.Cancel();
                await Should.ThrowAsync<OperationCanceledException>(() => activation.WaitAsync(DeliveryDeadline));
                runtime.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe("first");
                oldTarget.AcceptPostponed();
                foreach (var _ in new[] { queued, selected })
                {
                    await oldTarget.NextOfferAsync();
                    oldTarget.AcceptPostponed();
                }
                oldTarget.Accepted.ShouldBe([claimed, queued, selected]);
                (await selectedDispatch).Message.ShouldNotBeNull().MessageId.ShouldBe(selected.MessageId);
                replacement.TryReceive(out _).ShouldBeFalse();
            }
            else
            {
                oldTarget.AcceptPostponed();
                await using var nextLease = await activation.WaitAsync(DeliveryDeadline);
                runtime.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe("next");
                oldTarget.Accepted.ShouldHaveSingleItem().ShouldBeSameAs(claimed);
                (await replacement.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(queued);
                (await replacement.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(selected);
                (await selectedDispatch).Message.ShouldNotBeNull().MessageId.ShouldBe(selected.MessageId);

                // Later dispatch uses the new snapshot, not the retained old selection.
                var later = Message("after-revision");
                var delivered = runtime.ReceiveAsync<FlowValue>(Output, DeliveryDeadline);
                source.Post(later).ShouldBeTrue();
                (await healthy.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(later);
                (await delivered).Message.ShouldNotBeNull().MessageId.ShouldBe(later.MessageId);
                replacement.TryReceive(out _).ShouldBeFalse();
            }
        }
        finally { await runtime.DisposeAsync(); }
    }

    [Fact]
    public async Task Delivery_policy_only_changes_participate_in_revision_identity()
    {
        var link = CompileLinks("\"Sink.Input\"", ["Sink"]).Single();
        await using var runtime = new ApplicationPortRuntimeBuilder().AddOutput(Output).AddInput(link.Target).Build();
        var current = runtime.PrepareRevisionRouting([link]);
        var next = runtime.PrepareRevisionRouting([link.WithDelivery(FlowDeliveryPolicy.IsolateRejection)]);
        current.GetChangedSources(next).ShouldBe([Output]);
        current.GetChangedTargets(next).ShouldBe([link.Target]);
        current.GetChangedSources(runtime.PrepareRevisionRouting([link])).ShouldBeEmpty();
        Should.Throw<InvalidOperationException>(() => runtime.PrepareRevisionRouting(
            [link, link.WithDelivery(FlowDeliveryPolicy.IsolateRejection)])).Message.ShouldContain("duplicate link");
    }

    [Theory]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection)]
    public async Task Hosted_immediate_policy_reports_full_and_only_isolation_keeps_route_alive(FlowDeliveryPolicy policy)
    {
        var link = CompileLinks("\"Sink.Input\"", ["Sink"]).Single().WithDelivery(policy);
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var output = new ApplicationOutputPort(Output, 2, rejections.Enqueue, static _ => { }, new());
        var input = new ApplicationInputPort(link.Target, 2, rejections.Enqueue, static _ => { });
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 2 });
        await using var inputAttachment = await input.AttachAsync(target, CancellationToken.None);
        using var route = output.Connect(input, link);
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = output.Attach(source);
        var retained = Enumerable.Range(0, 4).Select(i => Message($"retained-{i}")).ToArray();
        var rejected = Message("full");
        try
        {
            foreach (var message in retained.Take(2)) target.Post(message).ShouldBeTrue();
            foreach (var message in retained.Skip(2))
                (await input.SendAsync(message, null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            using var dispatched = output.RegisterReceive();
            source.Post(rejected).ShouldBeTrue();
            if (policy == FlowDeliveryPolicy.RejectWhenFull)
            {
                var failure = await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(DeliveryDeadline));
                failure.Message.ShouldContain("Full");
            }
            else
            {
                (await dispatched.Task.WaitAsync(DeliveryDeadline)).Message.ShouldBeSameAs(rejected);
                output.Completion.IsCompleted.ShouldBeFalse();
            }
            var rejection = rejections.ShouldHaveSingleItem();
            rejection.Reason.ShouldBe(ApplicationPortRejectionReason.Full);
            rejection.Port.ShouldBe(link.Target);
            rejection.MessageId.ShouldBe(rejected.MessageId);
            foreach (var expected in retained)
                (await target.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(expected);
            if (policy == FlowDeliveryPolicy.IsolateRejection)
            {
                var later = Message("later");
                source.Post(later).ShouldBeTrue();
                (await target.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(later);
                await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(DeliveryDeadline);
                rejections.Count.ShouldBe(1);
            }
            target.TryReceive(out _).ShouldBeFalse();
        }
        finally { output.Abort(); input.Abort(); }
    }

    [Theory]
    [InlineData(FlowDeliveryPolicy.Reliable)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull)]
    [InlineData(FlowDeliveryPolicy.Reliable, true)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull, true)]
    public async Task Hosted_condition_failure_prevents_partial_fanout_and_preserves_original_failure(FlowDeliveryPolicy policy, bool fallback = false)
    {
        var healthyDeclaration = fallback ? "{\"Port\":\"Healthy.Input\",\"Fallback\":true}" : "\"Healthy.Input\"";
        var links = CompileLinks("[" + healthyDeclaration + ", {\"Port\":\"Failing.Input\",\"Condition\":\"fail\"}]",
            ["Healthy", "Failing"], new FailingExpressionEngine());
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var output = new ApplicationOutputPort(Output, 2, rejections.Enqueue, static _ => { }, new());
        var healthyLink = links.Single(link => !link.IsConditional);
        var failedLink = links.Single(link => link.IsConditional).WithDelivery(policy);
        var healthy = new ApplicationInputPort(healthyLink.Target, 2, rejections.Enqueue, static _ => { });
        var failed = new ApplicationInputPort(failedLink.Target, 2, rejections.Enqueue, static _ => { });
        var target = new BufferBlock<FlowMessage>();
        await using var attachment = await healthy.AttachAsync(target, CancellationToken.None);
        // Healthy is deliberately first: condition evaluation must finish before sending it anything.
        using var firstRoute = output.Connect(healthy, healthyLink);
        using var lastRoute = output.Connect(failed, failedLink);
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = output.Attach(source);
        var message = Message("conditional");
        try
        {
            source.Post(message).ShouldBeTrue();
            var exception = await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(DeliveryDeadline));
            var rejection = rejections.ShouldHaveSingleItem();
            exception.ShouldBeSameAs(rejection.Exception);
            exception.Message.ShouldBe("Condition failed.");
            rejection.RelatedPort.ShouldBe(failedLink.Target);
            rejection.MessageId.ShouldBe(message.MessageId);
            rejection.TraceId.ShouldBe(message.TraceId);
            target.TryReceive(out _).ShouldBeFalse();
        }
        finally { output.Abort(); healthy.Abort(); failed.Abort(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosted_unlink_or_abort_cancels_pending_admission_without_ghost_delivery(bool abort)
    {
        var links = CompileLinks("[\"Slow.Input\",\"Healthy.Input\"]", ["Slow", "Healthy"]);
        var output = new ApplicationOutputPort(Output, 2, static _ => { }, static _ => { }, new());
        var slow = new ApplicationInputPort(links[0].Target, 2, static _ => { }, static _ => { });
        var healthy = new ApplicationInputPort(links[1].Target, 2, static _ => { }, static _ => { });
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 2 });
        var sibling = new BufferBlock<FlowMessage>();
        await using var slowAttachment = await slow.AttachAsync(target, CancellationToken.None);
        await using var healthyAttachment = await healthy.AttachAsync(sibling, CancellationToken.None);
        using var slowRoute = output.Connect(slow, links[0]);
        using var healthyRoute = output.Connect(healthy, links[1]);
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = output.Attach(source);
        var retained = Enumerable.Range(0, 4).Select(i => Message($"retained-{i}")).ToArray();
        var canceled = Message("canceled");
        try
        {
            foreach (var message in retained.Take(2)) target.Post(message).ShouldBeTrue();
            foreach (var message in retained.Skip(2))
                (await slow.SendAsync(message, null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            source.Post(canceled).ShouldBeTrue();
            (await sibling.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(canceled);
            output.GetStatus().PendingMessages.ShouldBe(1);

            if (abort) output.Abort();
            else slowRoute.Dispose();
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(DeliveryDeadline);
            foreach (var expected in retained)
                (await target.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(expected);
            var sentinel = Message("after-cancellation");
            (await slow.SendAsync(sentinel, null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            (await target.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(sentinel);
            target.TryReceive(out _).ShouldBeFalse();
            if (!abort)
            {
                source.Post(sentinel).ShouldBeTrue();
                (await sibling.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(sentinel);
                await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(DeliveryDeadline);
                target.TryReceive(out _).ShouldBeFalse();
                output.Completion.IsCompleted.ShouldBeFalse();
            }
        }
        finally { output.Abort(); slow.Abort(); healthy.Abort(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosted_rejection_starts_healthy_siblings_and_cancels_blocked_admission(bool rejectFirst)
    {
        var links = CompileLinks("[\"Slow.Input\",\"Healthy.Input\",\"Missing.Input\"]", ["Slow", "Healthy", "Missing"]);
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var output = new ApplicationOutputPort(Output, 2, rejections.Enqueue, static _ => { }, new());
        var slow = new ApplicationInputPort(links[0].Target, 2, rejections.Enqueue, static _ => { });
        var healthy = new ApplicationInputPort(links[1].Target, 2, rejections.Enqueue, static _ => { });
        var missing = new ApplicationInputPort(links[2].Target, 2, rejections.Enqueue, static _ => { });
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 2 });
        var sibling = new BufferBlock<FlowMessage>();
        await using var slowAttachment = await slow.AttachAsync(target, CancellationToken.None);
        await using var healthyAttachment = await healthy.AttachAsync(sibling, CancellationToken.None);
        using var early = rejectFirst ? output.Connect(missing, links[2]) : null;
        using var slowRoute = output.Connect(slow, links[0]);
        using var healthyRoute = output.Connect(healthy, links[1]);
        using var late = !rejectFirst ? output.Connect(missing, links[2]) : null;
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = output.Attach(source);
        var retained = Enumerable.Range(0, 4).Select(i => Message($"retained-{i}")).ToArray();
        var message = Message("failed-fanout");
        try
        {
            foreach (var item in retained.Take(2)) target.Post(item).ShouldBeTrue();
            foreach (var item in retained.Skip(2))
                (await slow.SendAsync(item, null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            source.Post(message).ShouldBeTrue();
            var failure = await Should.ThrowAsync<InvalidOperationException>(() => output.Completion.WaitAsync(DeliveryDeadline));
            failure.Message.ShouldContain("Unavailable");
            failure.Message.ShouldContain(missing.Address.Value);
            (await sibling.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(message);
            var rejection = rejections.ShouldHaveSingleItem();
            rejection.Port.ShouldBe(missing.Address);
            rejection.MessageId.ShouldBe(message.MessageId);
            foreach (var expected in retained)
                (await target.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(expected);
            var sentinel = Message("after-failure");
            (await slow.SendAsync(sentinel, null, CancellationToken.None)).IsAccepted.ShouldBeTrue();
            (await target.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(sentinel);
            target.TryReceive(out _).ShouldBeFalse();
        }
        finally { output.Abort(); slow.Abort(); healthy.Abort(); missing.Abort(); }
    }

    [Fact]
    public void Compiled_delivery_policy_copy_preserves_condition_and_rejects_unknown_values()
    {
        var original = CompileLinks("{\"Port\":\"Sink.Input\",\"Condition\":\"fail\"}", ["Sink"], new FailingExpressionEngine()).Single();
        var copy = original.WithDelivery(FlowDeliveryPolicy.IsolateRejection);
        copy.ShouldNotBeSameAs(original);
        copy.Delivery.ShouldBe(FlowDeliveryPolicy.IsolateRejection);
        original.Delivery.ShouldBe(FlowDeliveryPolicy.Reliable);
        copy.Source.ShouldBe(original.Source);
        copy.Target.ShouldBe(original.Target);
        copy.MessageType.ShouldBe(original.MessageType);
        copy.ConditionExpression.ShouldBe(original.ConditionExpression);
        copy.DeclarationSide.ShouldBe(original.DeclarationSide);
        copy.TryMatch(new FlowMapContext(), out var exception).ShouldBeFalse();
        exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("Condition failed.");
        Should.Throw<ArgumentOutOfRangeException>(() => original.WithDelivery((FlowDeliveryPolicy)99)).ParamName.ShouldBe("delivery");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Reliable_hosted_fanout_waits_for_capacity_without_blocking_sibling_start(int capacity)
    {
        var links = CompileLinks("[\"Slow.Input\", \"Healthy.Input\"]", ["Slow", "Healthy"]);
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var output = new ApplicationOutputPort(Output, 2, rejections.Enqueue, static _ => { }, new());
        var slow = new ApplicationInputPort(links[0].Target, capacity, rejections.Enqueue, static _ => { });
        var healthy = new ApplicationInputPort(links[1].Target, capacity, rejections.Enqueue, static _ => { });
        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = capacity });
        var sibling = new BufferBlock<FlowMessage>();
        await using var slowAttachment = await slow.AttachAsync(target, CancellationToken.None);
        await using var healthyAttachment = await healthy.AttachAsync(sibling, CancellationToken.None);
        using var slowRoute = output.Connect(slow, links[0]);
        using var healthyRoute = output.Connect(healthy, links[1]);
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = output.Attach(source);
        var warmup = Enumerable.Range(0, capacity).Select(i => Message($"target-{i}")).ToArray();
        var queued = Enumerable.Range(0, capacity).Select(i => Message($"input-{i}")).ToArray();
        var routed = Message("routed");
        try
        {
            foreach (var message in warmup) target.Post(message).ShouldBeTrue();
            foreach (var message in queued)
                (await slow.SendAsync(message, null, CancellationToken.None)).IsAccepted.ShouldBeTrue();

            source.Post(routed).ShouldBeTrue();
            (await sibling.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(routed);
            slow.GetStatus().PendingMessages.ShouldBe(capacity);
            output.GetStatus().PendingMessages.ShouldBe(1);
            rejections.ShouldBeEmpty();

            foreach (var expected in warmup.Concat(queued).Append(routed))
                (await target.ReceiveAsync().WaitAsync(DeliveryDeadline)).ShouldBeSameAs(expected);
            await output.DrainAsync(CancellationToken.None).AsTask().WaitAsync(DeliveryDeadline);
            target.TryReceive(out _).ShouldBeFalse();
            sibling.TryReceive(out _).ShouldBeFalse();
            rejections.ShouldBeEmpty();
        }
        finally { output.Abort(); slow.Abort(); healthy.Abort(); }
    }
}
