using System.Collections.Concurrent;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed partial class InstanceFirstGraphTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Isolated_full_target_preserves_siblings_and_recovers(bool nativeSource)
    {
        var source = new ManualRouteSource(nativeSource);
        var full = new BufferedSink();
        var occupied = Value(1);
        full.Block.Post(occupied).ShouldBeTrue();
        var healthy = new RecordingSink();
        var reports = new ConcurrentQueue<(FlowMessage Message, Exception Error)>();
        var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("full", full).Input, delivery: FlowDeliveryPolicy.IsolateRejection,
            onRejected: (message, error) => reports.Enqueue((message, error)));
        output.LinkTo(workflow.Add("healthy", healthy).Input);
        await app.StartAsync();
        try
        {
            var first = Value(2);
            var second = Value(3);
            (await source.SendAsync(first)).ShouldBeTrue();
            (await source.SendAsync(second)).ShouldBeTrue();
            await healthy.SecondReceived.Task.WaitAsync(TestDeadline);
            healthy.Messages.ToArray()[0].ShouldBeSameAs(first);
            healthy.Messages.ToArray()[1].ShouldBeSameAs(second);
            var rejected = reports.ToArray();
            rejected.Length.ShouldBe(2);
            rejected[0].Message.ShouldBeSameAs(first);
            rejected[1].Message.ShouldBeSameAs(second);
            foreach (var report in rejected)
            {
                report.Error.ShouldBeOfType<InvalidOperationException>();
                report.Error.Message.ShouldContain("main.source.Output");
                report.Error.Message.ShouldContain("main.full.Input");
            }
            full.Block.Count.ShouldBe(1);
            app.Completion.IsCompleted.ShouldBeFalse();
            (await full.Block.ReceiveAsync().WaitAsync(TestDeadline)).ShouldBeSameAs(occupied);

            var recovered = Value(4);
            (await source.SendAsync(recovered)).ShouldBeTrue();
            (await full.Block.ReceiveAsync().WaitAsync(TestDeadline)).ShouldBeSameAs(recovered);
            source.Complete();
            await app.Completion.WaitAsync(TestDeadline);
            reports.Count.ShouldBe(2);
            healthy.Messages.Count.ShouldBe(3);
            healthy.Messages.Last().ShouldBeSameAs(recovered);
            full.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            healthy.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        finally
        {
            // A failed assertion must not strand a native buffer's graceful drain.
            using var drain = full.Block.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
            source.Complete();
            await app.DisposeAsync().AsTask().WaitAsync(TestDeadline);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Isolated_rejection_reports_each_original_message_without_detaching(bool nativeSource,
        bool throwsOnAdmission, bool throwsInHandler)
    {
        var messages = new[] { Value(5), Value(6) };
        var failure = new AggregateException("admission failure", new InvalidOperationException("inner failure"));
        var rejected = new ProbeSink(DataflowMessageStatus.DecliningPermanently,
            admissionFailure: throwsOnAdmission ? failure : null);
        var sibling = new ProbeSink();
        var fallback = new ProbeSink();
        var reports = new ConcurrentQueue<(FlowMessage Message, Exception Error)>();
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        IFlowComponent source = nativeSource ? new NativeSequenceSource(messages) : new SequenceSource(messages);
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("rejected", rejected).Input, delivery: FlowDeliveryPolicy.IsolateRejection,
            onRejected: (message, error) =>
            {
                reports.Enqueue((message, error));
                if (throwsInHandler) throw new InvalidOperationException("reporting failed");
            });
        output.LinkTo(workflow.Add("sibling", sibling).Input);
        output.OtherwiseTo(workflow.Add("fallback", fallback).Input);
        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);

        rejected.Offers.ShouldBe(2);
        rejected.InitialOffers.ShouldBe(2);
        rejected.Messages.ShouldBeEmpty();
        sibling.Messages.Count.ShouldBe(2);
        fallback.Offers.ShouldBe(0); // Fallback is not retry after admission failure.
        var reported = reports.ToArray();
        reported.Length.ShouldBe(2);
        for (var index = 0; index < messages.Length; index++)
        {
            reported[index].Message.ShouldBeSameAs(messages[index]);
            sibling.Messages.ToArray()[index].ShouldBeSameAs(messages[index]);
            if (throwsOnAdmission) reported[index].Error.ShouldBeSameAs(failure);
            else reported[index].Error.ShouldBeOfType<InvalidOperationException>();
        }
        rejected.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        sibling.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Isolated_condition_failure_allows_fallback_and_later_matching(bool nativeSource, bool hasFallback)
    {
        var messages = new[] { Value(7), Value(8), Value(9) };
        var error = new InvalidOperationException("condition failure");
        var reports = new ConcurrentQueue<(FlowMessage Message, Exception Error)>();
        var target = new ProbeSink();
        var fallback = new ProbeSink();
        var calls = 0;
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        IFlowComponent source = nativeSource ? new NativeSequenceSource(messages) : new SequenceSource(messages);
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("target", target).Input, message =>
        {
            calls++;
            if (ReferenceEquals(message, messages[1])) throw error;
            return true;
        }, FlowDeliveryPolicy.IsolateRejection, (message, failure) => reports.Enqueue((message, failure)));
        if (hasFallback) output.OtherwiseTo(workflow.Add("fallback", fallback).Input);
        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);

        calls.ShouldBe(3);
        reports.Count.ShouldBe(1);
        reports.Single().Message.ShouldBeSameAs(messages[1]);
        reports.Single().Error.ShouldBeSameAs(error);
        target.Offers.ShouldBe(2);
        target.Messages.ToArray()[0].ShouldBeSameAs(messages[0]);
        target.Messages.ToArray()[1].ShouldBeSameAs(messages[2]);
        fallback.Offers.ShouldBe(hasFallback ? 1 : 0);
        if (hasFallback) fallback.Messages.Single().ShouldBeSameAs(messages[1]);
        target.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Isolated_fallback_reports_rejection_without_offering_unselected_route(bool nativeSource)
    {
        var messages = new[] { Value(11), Value(12) };
        var ordinary = new ProbeSink();
        var fallback = new ProbeSink(DataflowMessageStatus.Declined);
        var reports = new ConcurrentQueue<(FlowMessage Message, Exception Error)>();
        var calls = 0;
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        IFlowComponent source = nativeSource ? new NativeSequenceSource(messages) : new SequenceSource(messages);
        var output = workflow.Add("source", source).Output;
        workflow.Add("ordinary", ordinary).Input.LinkFrom(output, _ => { calls++; return false; });
        output.OtherwiseTo(workflow.Add("fallback", fallback).Input, FlowDeliveryPolicy.IsolateRejection,
            (message, error) => reports.Enqueue((message, error)));
        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);

        calls.ShouldBe(2);
        ordinary.Offers.ShouldBe(0);
        fallback.Offers.ShouldBe(2);
        fallback.InitialOffers.ShouldBe(2);
        fallback.Messages.ShouldBeEmpty();
        var rejected = reports.ToArray();
        rejected.Length.ShouldBe(2);
        rejected[0].Message.ShouldBeSameAs(messages[0]);
        rejected[1].Message.ShouldBeSameAs(messages[1]);
        foreach (var report in rejected)
        {
            report.Error.ShouldBeOfType<InvalidOperationException>();
            report.Error.Message.ShouldContain("main.fallback.Input");
            report.Error.Message.ShouldContain("main.source.Output");
        }
        fallback.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, FlowDeliveryPolicy.Reliable)]
    [InlineData(true, FlowDeliveryPolicy.Reliable)]
    [InlineData(false, FlowDeliveryPolicy.RejectWhenFull)]
    [InlineData(true, FlowDeliveryPolicy.RejectWhenFull)]
    public async Task Strict_condition_failure_does_not_deliver_partially_selected_routes(bool nativeSource,
        FlowDeliveryPolicy policy)
    {
        var message = Value(13);
        var error = new InvalidOperationException("strict condition failure");
        var selected = new ProbeSink();
        var failing = new ProbeSink();
        var unvisited = new ProbeSink();
        var calls = 0;
        var reports = 0;
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        IFlowComponent source = nativeSource ? new NativeSequenceSource([message]) : new SequenceSource([message]);
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("selected", selected).Input);
        output.LinkTo(workflow.Add("failing", failing).Input, _ => { calls++; throw error; }, policy);
        output.LinkTo(workflow.Add("unvisited", unvisited).Input, _ => { calls++; return true; },
            FlowDeliveryPolicy.IsolateRejection, (_, _) => reports++);
        await app.StartAsync();

        (await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(TestDeadline))).ShouldBeSameAs(error);
        calls.ShouldBe(1);
        reports.ShouldBe(0);
        selected.Offers.ShouldBe(0);
        failing.Offers.ShouldBe(0);
        unvisited.Offers.ShouldBe(0);
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(TestDeadline));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Isolated_condition_failure_and_throwing_handler_preserve_siblings(bool nativeSource)
    {
        var message = Value(9);
        var error = new InvalidOperationException("condition failure");
        var target = new ProbeSink();
        var sibling = new ProbeSink();
        var reports = new ConcurrentQueue<(FlowMessage Message, Exception Error)>();
        var calls = 0;
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        IFlowComponent source = nativeSource ? new NativeSequenceSource([message, message]) : new SequenceSource([message, message]);
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("target", target).Input, _ => { calls++; throw error; },
            FlowDeliveryPolicy.IsolateRejection, (rejected, failure) =>
            {
                reports.Enqueue((rejected, failure));
                throw new InvalidOperationException("reporting failed");
            });
        output.LinkTo(workflow.Add("sibling", sibling).Input);
        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);

        calls.ShouldBe(2);
        reports.Count.ShouldBe(2);
        reports.All(report => ReferenceEquals(report.Message, message) && ReferenceEquals(report.Error, error)).ShouldBeTrue();
        target.Offers.ShouldBe(0);
        sibling.Messages.Count.ShouldBe(2);
        sibling.Messages.All(received => ReferenceEquals(received, message)).ShouldBeTrue();
        sibling.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Isolated_false_condition_still_requires_explicit_unmatched_policy()
    {
        var reports = 0;
        var target = new ProbeSink();
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        workflow.Add("source", new SequenceSource([Value(10)])).Output.LinkTo(workflow.Add("target", target).Input,
            _ => false, FlowDeliveryPolicy.IsolateRejection, (_, _) => reports++);
        await app.StartAsync();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(TestDeadline));
        error.Message.ShouldContain("No route accepted");
        error.Message.ShouldContain("main.source.Output");
        reports.ShouldBe(0);
        target.Offers.ShouldBe(0);
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(TestDeadline));
    }

    [Fact]
    public async Task Isolated_route_requires_handler_and_preserves_symmetric_link_metadata()
    {
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", new SequenceSource([])).Output;
        var input = workflow.Add("target", new ProbeSink()).Input;
        Predicate<FlowMessage> condition = _ => true;
        Action<FlowMessage, Exception> handler = (_, _) => { };
        Should.Throw<ArgumentNullException>(() => output.LinkTo(input, delivery: FlowDeliveryPolicy.IsolateRejection)).ParamName.ShouldBe("onRejected");
        Should.Throw<ArgumentNullException>(() => input.LinkFrom(output, delivery: FlowDeliveryPolicy.IsolateRejection)).ParamName.ShouldBe("onRejected");
        Should.Throw<ArgumentNullException>(() => output.OtherwiseTo(input, FlowDeliveryPolicy.IsolateRejection)).ParamName.ShouldBe("onRejected");
        foreach (var policy in new[] { FlowDeliveryPolicy.Reliable, FlowDeliveryPolicy.RejectWhenFull })
        {
            Should.Throw<ArgumentException>(() => output.LinkTo(input, delivery: policy, onRejected: handler)).ParamName.ShouldBe("onRejected");
            Should.Throw<ArgumentException>(() => input.LinkFrom(output, delivery: policy, onRejected: handler)).ParamName.ShouldBe("onRejected");
            Should.Throw<ArgumentException>(() => output.OtherwiseTo(input, policy, handler)).ParamName.ShouldBe("onRejected");
        }
        Should.Throw<ArgumentOutOfRangeException>(() => output.LinkTo(input, delivery: (FlowDeliveryPolicy)99, onRejected: handler)).ParamName.ShouldBe("delivery");
        var links = new[]
        {
            output.LinkTo(input, condition, FlowDeliveryPolicy.IsolateRejection, handler),
            input.LinkFrom(output, condition, FlowDeliveryPolicy.IsolateRejection, handler),
            output.OtherwiseTo(input, FlowDeliveryPolicy.IsolateRejection, handler)
        };
        foreach (var link in links)
        {
            link.Output.ShouldBeSameAs(output);
            link.Input.ShouldBeSameAs(input);
            link.Delivery.ShouldBe(FlowDeliveryPolicy.IsolateRejection);
            link.OnRejected.ShouldBeSameAs(handler);
        }
        links[0].Condition.ShouldBeSameAs(condition);
        links[1].Condition.ShouldBeSameAs(condition);
        links[0].IsFallback.ShouldBeFalse();
        links[1].IsFallback.ShouldBeFalse();
        links[2].Condition.ShouldBeNull();
        links[2].IsFallback.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => output.OtherwiseTo(input, FlowDeliveryPolicy.IsolateRejection, handler));
        output.LinkTo(input).Delivery.ShouldBe(FlowDeliveryPolicy.Reliable);
        output.LinkTo(input).OnRejected.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Isolated_route_does_not_hide_component_lifetime_failure(bool nativeSource)
    {
        var error = new InvalidOperationException("component lifetime failure");
        var source = new ManualRouteSource(nativeSource);
        var target = new ProbeSink();
        var sibling = new ProbeSink();
        var reports = 0;
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("target", target).Input, delivery: FlowDeliveryPolicy.IsolateRejection,
            onRejected: (_, _) => reports++);
        output.LinkTo(workflow.Add("sibling", sibling).Input);
        await app.StartAsync();
        target.Fault(error);

        (await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(TestDeadline))).ShouldBeSameAs(error);
        (await Should.ThrowAsync<InvalidOperationException>(() => sibling.Completion.WaitAsync(TestDeadline))).ShouldBeSameAs(error);
        source.Completion.IsFaulted.ShouldBeTrue();
        reports.ShouldBe(0);
        sibling.Offers.ShouldBe(0);
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(TestDeadline));
    }

    private sealed class ManualRouteSource : IFlowComponent
    {
        private readonly ISourceBlock<FlowMessage> _output;
        public ManualRouteSource(bool native)
        {
            _output = native ? new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 2 })
                : new FlowOutput<FlowMessage>(new FlowOutputOptions { Capacity = 2 });
            Ports = new FlowPorts(outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { ["Output"] = _output });
        }
        public FlowPorts Ports { get; }
        public Task Completion => _output.Completion;
        public ValueTask<bool> SendAsync(FlowMessage message) => _output is FlowOutput<FlowMessage> standard
            ? standard.SendAsync(message) : new(((ITargetBlock<FlowMessage>)_output).SendAsync(message));
        public void Complete() => _output.Complete();
        public void Fault(Exception error) => _output.Fault(error);
        public async ValueTask DisposeAsync()
        {
            Complete();
            if (_output is IAsyncDisposable disposable) await disposable.DisposeAsync();
            else await Completion;
        }
    }
}
