using System.Text.Json;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed partial class InstanceFirstGraphTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Direct_instance_graph_routes_conditional_fanout_across_workflows()
    {
        var dependency = new BorrowedDependency();
        var left = new RecordingSink();
        var right = new RecordingSink();
        await using var app = new FlowApplication(linkCapacity: 2);
        var main = app.AddWorkflow("main");
        var reports = app.AddWorkflow("reports");
        var source = main.Add("source", new SequenceSource([Value(3), Value(5)]));
        var transformInstance = new Multiply(dependency);
        var transform = main.Add("double", transformInstance);
        var first = reports.Add("first", left);
        var second = reports.Add("second", right);
        source.Output.LinkTo(transform.Input);
        var link = first.Input.LinkFrom(transform.Output, message => !message.IsError);
        transform.Output.LinkTo(second.Input, message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0);

        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);

        transformInstance.ConstructorCalls.ShouldBe(1);
        dependency.Calls.ShouldBe(2);
        dependency.Disposed.ShouldBeFalse();
        app.GetInput("reports.second.Input").ShouldBeSameAs(second.Input);
        app.GetOutput("main.double.Output").ShouldBeSameAs(transform.Output);
        link.Input.ShouldBeSameAs(first.Input);
        link.Output.ShouldBeSameAs(transform.Output);
        left.Messages.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([6, 10]);
        right.Messages.Count.ShouldBe(2);
        left.Messages.Zip(right.Messages).All(pair => ReferenceEquals(pair.First, pair.Second)).ShouldBeTrue();
        left.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        right.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await app.DisposeAsync();
        dependency.Disposed.ShouldBeFalse();
        left.DisposeCalls.ShouldBe(1);
        right.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Fan_in_waits_for_every_producer()
    {
        var release = Signal();
        var first = Value(1);
        var second = Value(2);
        var sink = new RecordingSink();
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var early = workflow.Add("early", new SequenceSource([first]));
        var late = workflow.Add("late", new SequenceSource([second], release.Task));
        var target = workflow.Add("sink", sink);
        early.Output.LinkTo(target.Input);
        late.Output.LinkTo(target.Input);

        await app.StartAsync();
        try
        {
            await sink.FirstReceived.Task.WaitAsync(TestDeadline);
            await early.Instance.Completion.WaitAsync(TestDeadline);
            sink.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await app.Completion.WaitAsync(TestDeadline);
        sink.Messages.ToArray().ShouldBe([first, second]);
        sink.Messages.Last().ShouldBeSameAs(second);
    }

    [Fact]
    public async Task Unmatched_output_faults_the_graph()
    {
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var source = workflow.Add("source", new SequenceSource([Value(1)]));
        var target = workflow.Add("sink", new RecordingSink());
        source.Output.LinkTo(target.Input, _ => false);
        await app.StartAsync();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(TestDeadline));
        error.Message.ShouldContain("main.source.Output");
        error.Message.ShouldContain("No route");
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Fallback_and_explicit_discard_are_distinct_routes()
    {
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var ordinary = new RecordingSink();
        var fallback = new RecordingSink();
        var source = workflow.Add("source", new SequenceSource([Value(1), Value(2)]));
        source.Output.LinkTo(workflow.Add("ordinary", ordinary).Input, m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == 1);
        source.Output.OtherwiseTo(workflow.Add("fallback", fallback).Input);
        workflow.Add("discarded", new SequenceSource([Value(3)])).Output.DiscardUnmatched();
        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);
        ordinary.Messages.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([1]);
        fallback.Messages.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([2]);
    }

    [Fact]
    public async Task Owned_and_borrowed_components_have_explicit_disposal()
    {
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var owned = new RecordingSink();
        var borrowed = new RecordingSink();
        var rejected = new RecordingSink();
        workflow.Add("owned", owned);
        workflow.Add("borrowed", borrowed, ComponentOwnership.Borrowed);
        Should.Throw<ArgumentException>(() => workflow.Add("owned", rejected));
        await app.StartAsync();
        await app.DisposeAsync();
        owned.DisposeCalls.ShouldBe(1);
        borrowed.DisposeCalls.ShouldBe(0);
        borrowed.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        rejected.DisposeCalls.ShouldBe(0);
        rejected.Completion.IsCompleted.ShouldBeFalse();
        await app.DisposeAsync();
        owned.DisposeCalls.ShouldBe(1);
        await borrowed.DisposeAsync();
        await rejected.DisposeAsync();
    }

    [Fact]
    public async Task Topology_rejects_cycles_cross_graph_links_and_mutation_after_start()
    {
        await using var app = new FlowApplication();
        await using var other = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var source = new SequenceSource([]);
        workflow.Add("source", source).Output.DiscardUnmatched();
        var one = workflow.Add("one", new Relay());
        var two = workflow.Add("two", new Relay());
        var foreign = other.AddWorkflow("foreign").Add("sink", new RecordingSink());
        Should.Throw<ArgumentException>(() => one.Output.LinkTo(foreign.Input));
        Should.Throw<ArgumentException>(() => workflow.Add("again", source));
        one.Output.LinkTo(two.Input);
        two.Output.LinkTo(one.Input);
        Should.Throw<InvalidOperationException>(() => app.StartAsync()).Message.ShouldContain("cycle");
        source.Runs.ShouldBe(0);

        await using var valid = new FlowApplication();
        var validWorkflow = valid.AddWorkflow("valid");
        var sink = new RecordingSink();
        validWorkflow.Add("sink", sink);
        await valid.StartAsync();
        var notAdded = new RecordingSink();
        Should.Throw<InvalidOperationException>(() => validWorkflow.Add("late", notAdded));
        notAdded.DisposeCalls.ShouldBe(0);
        await notAdded.DisposeAsync();
    }

    [Fact]
    public async Task Errors_forward_the_same_envelope_without_processing()
    {
        var failure = FlowMessage.CreateError(new FlowError("test.failure", "expected", "test", false));
        var relay = new Relay();
        var sink = new RecordingSink();
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var source = workflow.Add("source", new SequenceSource([failure]));
        var middle = workflow.Add("relay", relay);
        source.Output.LinkTo(middle.Input);
        middle.Output.LinkTo(workflow.Add("sink", sink).Input, message => message.IsError);
        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);
        relay.ProcessCalls.ShouldBe(0);
        sink.Messages.Single().ShouldBeSameAs(failure);
        sink.Messages.Single().Error.ShouldBeSameAs(failure.Error);
    }

    [Fact]
    public async Task Stop_before_start_does_not_run_a_source()
    {
        var source = new SequenceSource([Value(1)]);
        var app = new FlowApplication();
        app.AddWorkflow("main").Add("source", source, ComponentOwnership.Borrowed);
        await app.StopAsync().WaitAsync(TestDeadline);
        source.Runs.ShouldBe(0);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => app.AddWorkflow("late"));
        Should.Throw<InvalidOperationException>(() => app.StartAsync());
        await app.DisposeAsync().AsTask().WaitAsync(TestDeadline);
        await source.DisposeAsync();
    }

    [Fact]
    public async Task Slow_target_does_not_prevent_a_healthy_sibling_offer()
    {
        var release = Signal();
        var slow = new RecordingSink(release.Task);
        var fast = new RecordingSink();
        var messages = new[] { Value(1), Value(2) };
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("main");
        var source = workflow.Add("source", new SequenceSource(messages));
        source.Output.LinkTo(workflow.Add("slow", slow).Input);
        source.Output.LinkTo(workflow.Add("fast", fast).Input);
        await app.StartAsync();
        try
        {
            await fast.SecondReceived.Task.WaitAsync(TestDeadline);
            slow.Messages.ShouldBeEmpty();
            app.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await app.Completion.WaitAsync(TestDeadline);
        slow.Messages.ToArray().ShouldBe(messages);
        fast.Messages.ToArray().ShouldBe(messages);
    }

    [Fact]
    public async Task Standard_graph_routes_share_component_output_completion()
    {
        var source = new SequenceSource([Value(7)]);
        var sink = new RecordingSink();
        await using var app = new FlowApplication(linkCapacity: 2);
        var workflow = app.AddWorkflow("main");
        workflow.Add("source", source).Output.LinkTo(workflow.Add("sink", sink).Input);
        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);
        sink.Messages.Single().Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(7);

        // This is deliberately a structural gate: a green facade over a second
        // output dispatcher does not satisfy the single-kernel requirement.
        var execution = typeof(FlowApplication).GetField("_execution", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(app).ShouldNotBeNull();
        var routers = (IDictionary)execution.GetType().GetField("_routers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(execution).ShouldNotBeNull();
        routers.Count.ShouldBe(1);
        var router = routers.Values.Cast<object>().Single();
        router.GetType().GetProperty("Completion")!.GetValue(router).ShouldBeSameAs(source.Output.Completion);
        router.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .ShouldNotContain(field => field.FieldType.IsGenericType &&
                field.FieldType.GetGenericTypeDefinition() == typeof(ActionBlock<>));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_message_instances_re_evaluate_routes_for_every_emission(bool nativeSource)
    {
        var message = Value(5);
        IFlowComponent source = nativeSource
            ? new NativeSequenceSource([message, message, message])
            : new SequenceSource([message, message, message]);
        var ordinary = new RecordingSink();
        var fallback = new RecordingSink();
        var ignored = new RecordingSink();
        var selectedCalls = 0;
        var ignoredCalls = 0;
        await using var app = new FlowApplication(linkCapacity: 2);
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("ordinary", ordinary).Input, _ => ++selectedCalls % 2 == 0);
        output.LinkTo(workflow.Add("ignored", ignored).Input, _ => { ignoredCalls++; return false; });
        output.OtherwiseTo(workflow.Add("fallback", fallback).Input);
        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);
        selectedCalls.ShouldBe(3);
        ignoredCalls.ShouldBe(3);
        ordinary.Messages.Single().ShouldBeSameAs(message);
        fallback.Messages.Count.ShouldBe(2);
        fallback.Messages.ShouldAllBe(value => ReferenceEquals(value, message));
        ignored.Messages.ShouldBeEmpty();
        fallback.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Native_sibling_is_offered_before_graph_selection_failure()
    {
        var message = Value(9);
        var failure = new InvalidOperationException("selection failed");
        var source = new SequenceSource([message]);
        var sink = new RecordingSink();
        var earlierRoute = new ProbeSink();
        var native = new BufferBlock<FlowMessage>();
        using var nativeLink = source.Output.LinkTo(native, new DataflowLinkOptions());
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("earlier", earlierRoute).Input, _ => true);
        output.LinkTo(workflow.Add("sink", sink).Input, _ => throw failure);
        await app.StartAsync();
        (await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(TestDeadline)))
            .ShouldBeSameAs(failure);
        (await native.ReceiveAsync().WaitAsync(TestDeadline)).ShouldBeSameAs(message);
        sink.Messages.ShouldBeEmpty();
        earlierRoute.Offers.ShouldBe(0);
        earlierRoute.Messages.ShouldBeEmpty();
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(TestDeadline));
    }

    [Theory]
    [InlineData(false, FlowDeliveryPolicy.Reliable, DataflowMessageStatus.Declined)]
    [InlineData(false, FlowDeliveryPolicy.Reliable, DataflowMessageStatus.DecliningPermanently)]
    [InlineData(true, FlowDeliveryPolicy.Reliable, DataflowMessageStatus.Declined)]
    [InlineData(true, FlowDeliveryPolicy.Reliable, DataflowMessageStatus.DecliningPermanently)]
    [InlineData(false, FlowDeliveryPolicy.RejectWhenFull, DataflowMessageStatus.Declined)]
    [InlineData(false, FlowDeliveryPolicy.RejectWhenFull, DataflowMessageStatus.DecliningPermanently)]
    [InlineData(true, FlowDeliveryPolicy.RejectWhenFull, DataflowMessageStatus.Declined)]
    [InlineData(true, FlowDeliveryPolicy.RejectWhenFull, DataflowMessageStatus.DecliningPermanently)]
    public async Task Selected_target_rejection_faults_after_sibling_offer(bool nativeSource,
        FlowDeliveryPolicy policy, DataflowMessageStatus rejection)
    {
        var message = Value(11);
        IFlowComponent source = nativeSource ? new NativeSequenceSource([message]) : new SequenceSource([message]);
        var rejected = new ProbeSink(rejection);
        var sibling = new ProbeSink();
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("rejected", rejected).Input, delivery: policy);
        output.LinkTo(workflow.Add("sibling", sibling).Input);
        await app.StartAsync();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(TestDeadline));
        error.Message.ShouldContain("main.source.Output");
        error.Message.ShouldContain("main.rejected.Input");
        var expectedOffers = policy == FlowDeliveryPolicy.Reliable && rejection == DataflowMessageStatus.Declined
            ? 2 : 1;
        rejected.Offers.ShouldBe(expectedOffers);
        rejected.InitialOffers.ShouldBe(1);
        rejected.Messages.ShouldBeEmpty();
        sibling.Messages.Single().ShouldBeSameAs(message);
        sibling.MessagesAtFault.ShouldBe(1);
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(TestDeadline));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unselected_rejecting_route_is_not_offered_and_fallback_delivers(bool nativeSource)
    {
        var message = Value(13);
        IFlowComponent source = nativeSource ? new NativeSequenceSource([message]) : new SequenceSource([message]);
        var rejected = new ProbeSink(DataflowMessageStatus.DecliningPermanently);
        var fallback = new ProbeSink();
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("rejected", rejected).Input, _ => false, FlowDeliveryPolicy.RejectWhenFull);
        output.OtherwiseTo(workflow.Add("fallback", fallback).Input, FlowDeliveryPolicy.RejectWhenFull);
        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);
        rejected.Offers.ShouldBe(0);
        fallback.Messages.Single().ShouldBeSameAs(message);
        fallback.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Graph_fault_releases_native_reservation_without_waiting_for_output_completion()
    {
        var reserved = new ProbeSink(reserveUntilFault: true);
        var sibling = new ProbeSink();
        var message = Value(17);
        var failure = new InvalidOperationException("reserved delivery failed");
        var source = new SequenceSource([message]);
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", source).Output;
        output.LinkTo(workflow.Add("reserved", reserved).Input);
        output.LinkTo(workflow.Add("sibling", sibling).Input);
        await app.StartAsync();
        try
        {
            await reserved.ReservationAttempted.Task.WaitAsync(TestDeadline);
            await sibling.Accepted.Task.WaitAsync(TestDeadline);
            reserved.Reserved.ShouldBeTrue();
            source.Output.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { source.Output.Fault(failure); }
        (await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(TestDeadline)))
            .ShouldBeSameAs(failure);
        reserved.Released.ShouldBeTrue();
        reserved.Messages.ShouldBeEmpty();
        sibling.Messages.Single().ShouldBeSameAs(message);
        sibling.MessagesAtFault.ShouldBe(1);
        await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(TestDeadline));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reject_when_full_does_not_wait_for_native_target_capacity(bool nativeSource)
    {
        var full = new BufferedSink();
        full.Block.Post(Value(19)).ShouldBeTrue();
        IFlowComponent source = nativeSource ? new NativeSequenceSource([Value(23)]) : new SequenceSource([Value(23)]);
        var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        workflow.Add("source", source).Output.LinkTo(workflow.Add("full", full).Input,
            delivery: FlowDeliveryPolicy.RejectWhenFull);
        await app.StartAsync();
        try
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(TestDeadline));
            error.Message.ShouldContain("main.full.Input");
            error.Message.ShouldContain("main.source.Output");
        }
        finally
        {
            full.Fault(new InvalidOperationException("test cleanup"));
            await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(TestDeadline));
        }
    }

    [Fact]
    public async Task Native_source_adapter_capacity_includes_the_active_delivery()
    {
        var source = new NativeSequenceSource([Value(29), Value(31), Value(37)]);
        var reserved = new ProbeSink(reserveUntilFault: true);
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("main");
        workflow.Add("source", source).Output.LinkTo(workflow.Add("reserved", reserved).Input);
        await app.StartAsync();
        try
        {
            await reserved.ReservationAttempted.Task.WaitAsync(TestDeadline);
            reserved.Reserved.ShouldBeTrue();
            source.PendingMessages.ShouldBe(2);
            source.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { reserved.Resume(); }
        await app.Completion.WaitAsync(TestDeadline);
        reserved.Messages.Select(message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).Order().ShouldBe([29, 31, 37]);
        source.PendingMessages.ShouldBe(0);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public async Task Graph_routes_preserve_native_immediate_admission(bool nativeSource, int targetCount)
    {
        var messages = new[] { Value(1), Value(2), Value(3) };
        IFlowComponent source = nativeSource ? new NativeSequenceSource(messages) : new SequenceSource(messages);
        var targets = Enumerable.Range(0, targetCount).Select(_ => new ProbeSink()).ToArray();
        await using var app = new FlowApplication();
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", source).Output;
        for (var index = 0; index < targets.Length; index++)
            output.LinkTo(workflow.Add("sink" + index, targets[index]).Input);

        await app.StartAsync();
        await app.Completion.WaitAsync(TestDeadline);

        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        foreach (var target in targets)
        {
            target.Messages.Select(message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([1, 2, 3]);
            target.Messages.Zip(messages).ShouldAllBe(pair => ReferenceEquals(pair.First, pair.Second));
            target.Offers.ShouldBe(messages.Length);
            target.InitialOffers.ShouldBe(messages.Length);
            target.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
    }

    private static FlowMessage Value(int value) => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    [Theory]
    [InlineData(1, ComponentOwnership.Owned, false, false)]
    [InlineData(3, ComponentOwnership.Borrowed, false, false)]
    [InlineData(1, ComponentOwnership.Owned, true, false)]
    [InlineData(3, ComponentOwnership.Borrowed, true, false)]
    [InlineData(1, ComponentOwnership.Owned, true, true)]
    [InlineData(3, ComponentOwnership.Borrowed, true, true)]
    public async Task Graph_fault_drains_completing_native_source_without_business_delivery(
        int capacity, ComponentOwnership ownership, bool timerFailure, bool internalFault)
    {
        var source = new NativeSequenceSource(Enumerable.Range(1, capacity + 3).Select(Value).ToArray());
        var held = new HeldTarget();
        var sibling = new ProbeSink();
        var failure = new InvalidOperationException("graph failed during native drain");
        var cleanupFailure = new InvalidOperationException("Exchange cleanup failed.");
        var clock = new ApplicationCleanupTimeProvider();
        var app = new FlowApplication(linkCapacity: capacity, timeProvider: clock);
        var workflow = app.AddWorkflow("main");
        var output = workflow.Add("source", source, ownership).Output;
        output.LinkTo(workflow.Add("held", held).Input);
        output.LinkTo(workflow.Add("sibling", sibling).Input);
        await app.StartAsync();
        try
        {
            await held.ReservationReady.Task.WaitAsync(TestDeadline);
            await sibling.Accepted.Task.WaitAsync(TestDeadline);
            held.Reserved.ShouldBeTrue();
            source.PendingMessages.ShouldBeGreaterThan(0);
            source.Completion.IsCompleted.ShouldBeFalse();
            clock.StopError = timerFailure ? cleanupFailure : null;
            if (internalFault)
            {
                sibling.Fault(failure);
                var completionError = await Should.ThrowAsync<AggregateException>(() => app.Completion.WaitAsync(TestDeadline));
                completionError.Flatten().InnerExceptions.ShouldBe([failure, cleanupFailure]);
            }
            else
            {
                if (timerFailure) Should.Throw<InvalidOperationException>(() => app.Fault(failure)).ShouldBeSameAs(cleanupFailure);
                else app.Fault(failure);
                (await Should.ThrowAsync<InvalidOperationException>(() => app.Completion.WaitAsync(TestDeadline)))
                    .ShouldBeSameAs(failure);
            }
            app.Fault(new InvalidOperationException("later failure"));
            held.Released.ShouldBeTrue();
            sibling.Messages.Select(message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([1]);
            source.PendingMessages.ShouldBe(0);
            source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            await Should.ThrowAsync<AggregateException>(() => app.DisposeAsync().AsTask().WaitAsync(TestDeadline));
            source.DisposeCalls.ShouldBe(ownership == ComponentOwnership.Owned ? 1 : 0);
        }
        finally
        {
            // Permit cleanup even when a regression leaves the native tail stranded.
            if (!app.Completion.IsCompleted) app.Fault(failure);
            source.DrainForCleanup();
            try { await app.DisposeAsync().AsTask().WaitAsync(TestDeadline); }
            catch (AggregateException) { }
            if (ownership == ComponentOwnership.Borrowed) await source.DisposeAsync();
        }
    }

    private sealed class HeldTarget : IFlowComponent, ITargetBlock<FlowMessage>
    {
        private readonly TaskCompletionSource _completion = Signal();
        private ISourceBlock<FlowMessage>? _source;
        private DataflowMessageHeader _header;
        private int _released;
        public FlowPorts Ports => new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = this });
        public Task Completion => _completion.Task;
        public TaskCompletionSource ReservationReady { get; } = Signal();
        public bool Reserved { get; private set; }
        public bool Released => Volatile.Read(ref _released) != 0;
        public DataflowMessageStatus OfferMessage(DataflowMessageHeader header, FlowMessage value,
            ISourceBlock<FlowMessage>? source, bool consumeToAccept)
        {
            if (_completion.Task.IsCompleted) return DataflowMessageStatus.DecliningPermanently;
            if (source is null) return DataflowMessageStatus.Declined;
            _source = source!;
            _header = header;
            Reserved = source!.ReserveMessage(header, this);
            ReservationReady.TrySetResult();
            return DataflowMessageStatus.Postponed;
        }
        public void Complete() { _completion.TrySetResult(); Release(); }
        public void Fault(Exception error) { _completion.TrySetException(error); Release(); }
        private void Release()
        {
            if (Reserved && Interlocked.Exchange(ref _released, 1) == 0)
                _source!.ReleaseReservation(_header, this);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BufferedSink : IFlowComponent
    {
        public BufferBlock<FlowMessage> Block { get; } = new(new DataflowBlockOptions { BoundedCapacity = 1 });
        public FlowPorts Ports => new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = Block });
        public Task Completion => Block.Completion;
        public void Complete() => Block.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)Block).Fault(error);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }

    // A synchronous protocol probe records admission before a subsequent graph fault
    // can discard queued input. It is not an alternate production delivery mechanism.
    private sealed class ProbeSink(DataflowMessageStatus? rejection = null, bool reserveUntilFault = false,
        Exception? admissionFailure = null)
        : IFlowComponent, ITargetBlock<FlowMessage>
    {
        private readonly TaskCompletionSource _completion = Signal();
        private ISourceBlock<FlowMessage>? _source;
        private DataflowMessageHeader _header;
        private bool _resumed;
        public FlowPorts Ports => new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = this });
        public Task Completion => _completion.Task;
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int Offers { get; private set; }
        public int InitialOffers { get; private set; }
        public int MessagesAtFault { get; private set; }
        public bool Reserved { get; private set; }
        public bool Released { get; private set; }
        public TaskCompletionSource ReservationAttempted { get; } = Signal();
        public TaskCompletionSource Accepted { get; } = Signal();
        public DataflowMessageStatus OfferMessage(DataflowMessageHeader header, FlowMessage value,
            ISourceBlock<FlowMessage>? source, bool consumeToAccept)
        {
            Offers++;
            if (source is null) InitialOffers++;
            if (admissionFailure is not null) throw admissionFailure;
            if (rejection is { } status) return status;
            if (reserveUntilFault && !_resumed)
            {
                if (source is null) return DataflowMessageStatus.Declined;
                _source = source!;
                _header = header;
                Reserved = source!.ReserveMessage(header, this);
                ReservationAttempted.TrySetResult();
                return DataflowMessageStatus.Postponed;
            }
            if (consumeToAccept)
            {
                value = source!.ConsumeMessage(header, this, out var consumed)!;
                if (!consumed) return DataflowMessageStatus.NotAvailable;
            }
            Messages.Enqueue(value);
            Accepted.TrySetResult();
            return DataflowMessageStatus.Accepted;
        }
        public void Complete() => _completion.TrySetResult();
        public void Resume()
        {
            _resumed = true;
            if (!Reserved || Released) return;
            var message = _source!.ConsumeMessage(_header, this, out var consumed);
            if (!consumed) throw new InvalidOperationException("The test reservation was not consumable.");
            Messages.Enqueue(message!);
            Accepted.TrySetResult();
        }
        public void Fault(Exception error)
        {
            MessagesAtFault = Messages.Count;
            if (Reserved && !Released)
            {
                _source!.ReleaseReservation(_header, this);
                Released = true;
            }
            _completion.TrySetException(error);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NativeSequenceSource(FlowMessage[] messages) : IFlowComponent, IFlowSource
    {
        private readonly BufferBlock<FlowMessage> _output = new(new DataflowBlockOptions { BoundedCapacity = 8 });
        public int PendingMessages => _output.Count;
        public int DisposeCalls { get; private set; }
        public void DrainForCleanup() => _output.TryReceiveAll(out _);
        public FlowPorts Ports => new(outputs: new Dictionary<string, ISourceBlock<FlowMessage>> { ["Output"] = _output });
        public Task Completion => _output.Completion;
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var message in messages)
                if (!_output.Post(message)) throw new InvalidOperationException("Native source capacity exceeded.");
            _output.Complete();
            return Task.CompletedTask;
        }
        public void Complete() => _output.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_output).Fault(error);
        public async ValueTask DisposeAsync() { DisposeCalls++; Complete(); try { await Completion; } catch { } }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class SequenceSource(FlowMessage[] messages, Task? ready = null) : FlowSource
    {
        public int Runs { get; private set; }
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            Runs++;
            if (ready is not null) await ready.WaitAsync(cancellationToken);
            foreach (var message in messages) await EmitAsync(message, cancellationToken);
        }
    }

    private sealed class Relay : FlowNode
    {
        public int ProcessCalls { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message)
        {
            ProcessCalls++;
            await EmitAsync(message);
        }
    }

    private sealed class BorrowedDependency : IDisposable
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public int Double(int value) { Calls++; return value * 2; }
        public void Dispose() => Disposed = true;
    }

    private sealed class Multiply : FlowNode
    {
        private readonly BorrowedDependency _dependency;
        public Multiply(BorrowedDependency dependency) { _dependency = dependency; ConstructorCalls++; }
        public int ConstructorCalls { get; }
        protected override async Task ProcessAsync(FlowMessage message)
            => await EmitAsync(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(_dependency.Double(message.Value!.Json.Deserialize<int>()), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
    }

    // A sink needs only its actual input and block lifetime. No base class, DI, descriptor,
    // creator, materializer, output/event placeholder or engine integration is required.
    private sealed class RecordingSink : IFlowComponent
    {
        private readonly ActionBlock<FlowMessage> _input;
        public RecordingSink(Task? release = null)
        {
            _input = new ActionBlock<FlowMessage>(async message =>
            {
                if (release is not null) await release;
                Messages.Enqueue(message);
                FirstReceived.TrySetResult();
                if (Messages.Count == 2) SecondReceived.TrySetResult();
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = 1 });
            Ports = new FlowPorts(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });
        }
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public TaskCompletionSource FirstReceived { get; } = Signal();
        public TaskCompletionSource SecondReceived { get; } = Signal();
        public int DisposeCalls { get; private set; }
        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_input).Fault(exception);
        public async ValueTask DisposeAsync() { DisposeCalls++; Complete(); await Completion; }
    }
}
