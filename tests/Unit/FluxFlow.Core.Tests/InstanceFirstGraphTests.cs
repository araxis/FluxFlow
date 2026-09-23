using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using System.Text.Json;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Core.Tests;

public sealed partial class InstanceFirstGraphTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Native_DI_routes_conditional_fanout_across_workflows()
    {
        var dependency = new BorrowedDependency();
        var left = new RecordingSink();
        var right = new RecordingSink();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", new SequenceSource([Value(3), Value(5)]));
        var transformInstance = new Multiply(dependency);
        RegisterNode(services, "main.double", transformInstance);
        RegisterSink(services, "reports.first", left);
        RegisterSink(services, "reports.second", right);
        FlowLink? link = null;
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.double.Input");
            link = ports.Link("main.double.Output", "reports.first.Input", message => !message.IsError);
            ports.Link("main.double.Output", "reports.second.Input", message => message.Value.Json.GetInt32() > 0);
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync(linkCapacity: 2);
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);

        transformInstance.ConstructorCalls.ShouldBe(1);
        dependency.Calls.ShouldBe(2);
        dependency.Disposed.ShouldBeFalse();
        provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("reports.second.Input").ShouldBeSameAs(right.Input);
        provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.double.Output").ShouldBeSameAs(transformInstance.Output);
        link.ShouldNotBeNull().InputAddress.ShouldBe("reports.first.Input");
        link.Input.ShouldBeSameAs(left.Input);
        link.Output.ShouldBeSameAs(transformInstance.Output);
        link.OutputAddress.ShouldBe("main.double.Output");
        left.Messages.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([6, 10]);
        right.Messages.Count.ShouldBe(2);
        left.Messages.Zip(right.Messages).All(pair => ReferenceEquals(pair.First, pair.Second)).ShouldBeTrue();
        left.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        right.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await appExecution.DisposeAsync();
        left.DisposeCalls.ShouldBe(0);
        right.DisposeCalls.ShouldBe(0);
        await provider.DisposeAsync();
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
        var services = new ServiceCollection();
        var early = new SequenceSource([first]);
        RegisterSource(services, "main.early", early);
        RegisterSource(services, "main.late", new SequenceSource([second], release.Task));
        RegisterSink(services, "main.sink", sink);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.early.Output", "main.sink.Input");
            ports.Link("main.late.Output", "main.sink.Input");
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        try
        {
            await sink.FirstReceived.Task.WaitAsync(TestDeadline);
            await early.Completion.WaitAsync(TestDeadline);
            sink.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await appExecution.Completion.WaitAsync(TestDeadline);
        sink.Messages.ToArray().ShouldBe([first, second]);
        sink.Messages.Last().ShouldBeSameAs(second);
    }

    [Fact]
    public async Task Unmatched_output_faults_the_graph()
    {
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", new SequenceSource([Value(1)]));
        RegisterSink(services, "main.sink", new RecordingSink());
        services.AddSingleton<LinkFactory>(ports => ports.Link("main.source.Output", "main.sink.Input", _ => false));
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline));
        error.Message.ShouldContain("main.source.Output");
        error.Message.ShouldContain("No route");
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask());
        await Should.ThrowAsync<Exception>(() => provider.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Fallback_and_terminal_sink_are_distinct_routes()
    {
        var services = new ServiceCollection();
        var ordinary = new RecordingSink();
        var fallback = new RecordingSink();
        RegisterSource(services, "main.source", new SequenceSource([Value(1), Value(2)]));
        RegisterSink(services, "main.ordinary", ordinary);
        RegisterSink(services, "main.fallback", fallback);
        RegisterSource(services, "main.discarded", new SequenceSource([Value(3)]));
        TestSink.Register(services, "main.discarded.Output");
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.ordinary.Input", message => message.Value.Json.GetInt32() == 1);
            ports.LinkFallback("main.source.Output", "main.fallback.Input");
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
        ordinary.Messages.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([1]);
        fallback.Messages.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([2]);
    }

    [Fact]
    public async Task Registered_components_are_disposed_once_and_rejected_instances_stay_with_the_caller()
    {
        var services = new ServiceCollection();
        var first = new RecordingSink();
        var second = new RecordingSink();
        var rejected = new RecordingSink();
        RegisterSink(services, "main.first", first);
        RegisterSink(services, "main.second", second);
        Should.Throw<ArgumentException>(() => RegisterSink(services, "main.first", rejected));
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.DisposeAsync();
        first.DisposeCalls.ShouldBe(0);
        second.DisposeCalls.ShouldBe(0);
        await provider.DisposeAsync();
        first.DisposeCalls.ShouldBe(1);
        second.DisposeCalls.ShouldBe(1);
        second.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        rejected.DisposeCalls.ShouldBe(0);
        rejected.Completion.IsCompleted.ShouldBeFalse();
        await appExecution.DisposeAsync();
        first.DisposeCalls.ShouldBe(1);
        second.DisposeCalls.ShouldBe(1);
        await rejected.DisposeAsync();
    }

    [Fact]
    public async Task Composition_rejects_cycles_foreign_ports_and_route_mutation_after_start()
    {
        var services = new ServiceCollection();
        var otherServices = new ServiceCollection();
        var foreign = new RecordingSink();
        RegisterSink(otherServices, "foreign.sink", foreign);
        await using var otherProvider = otherServices.BuildServiceProvider();
        await using var otherExecution = await otherProvider.BuildApplicationAsync();
        var source = new SequenceSource([]);
        var one = new Relay();
        RegisterSource(services, "main.source", source);
        RegisterNode(services, "main.one", one);
        RegisterNode(services, "main.two", new Relay());
        services.AddSingleton<LinkFactory>(ports =>
        {
            Should.Throw<KeyNotFoundException>(() => ports.Link("main.one.Output", "foreign.sink.Input"));
            var foreignRoute = new FlowLink("main.one.Output", one.Output, "main.two.Input", foreign.Input,
                null, FlowDeliveryPolicy.Reliable, false, null);
            Should.Throw<ArgumentException>(() => ports.GetRequiredKeyedService<Action<FlowLink>>("main.one.Output")(foreignRoute));
            ports.Link("main.one.Output", "main.two.Input");
            ports.Link("main.two.Output", "main.one.Input");
        });
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync())).Message.ShouldContain("cycle");
        source.Runs.ShouldBe(0);

        var validServices = new ServiceCollection();
        RegisterNode(validServices, "valid.relay", new Relay());
        RegisterSink(validServices, "valid.sink", new RecordingSink());
        validServices.AddSingleton<LinkFactory>(ports => ports.Link("valid.relay.Output", "valid.sink.Input"));
        await using var validProvider = validServices.BuildServiceProvider();
        await using var validExecution = await validProvider.BuildApplicationAsync();
        await validExecution.StartAsync();
        Should.Throw<InvalidOperationException>(() => validProvider.Link("valid.relay.Output", "valid.sink.Input"));
    }

    [Fact]
    public async Task Errors_forward_the_same_envelope_without_processing()
    {
        var failure = FlowMessage.Create(new FlowError("test.failure", "expected", "test", false));
        var relay = new Relay();
        var sink = new RecordingSink();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", new SequenceSource([failure]));
        RegisterNode(services, "main.relay", relay);
        RegisterSink(services, "main.sink", sink);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.relay.Input");
            ports.Link("main.relay.Output", "main.sink.Input", message => message.IsError);
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
        relay.ProcessCalls.ShouldBe(0);
        sink.Messages.Single().ShouldBeSameAs(failure);
        sink.Messages.Single().Error.ShouldBeSameAs(failure.Error);
    }

    [Fact]
    public async Task Stop_before_start_does_not_run_a_source()
    {
        var source = new SequenceSource([Value(1)]);
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StopAsync().WaitAsync(TestDeadline);
        source.Runs.ShouldBe(0);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => appExecution.StartAsync());
        await appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline);
    }

    [Fact]
    public async Task Slow_target_does_not_prevent_a_healthy_sibling_offer()
    {
        var release = Signal();
        var slow = new RecordingSink(release.Task);
        var fast = new RecordingSink();
        var messages = new[] { Value(1), Value(2) };
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", new SequenceSource(messages));
        RegisterSink(services, "main.slow", slow);
        RegisterSink(services, "main.fast", fast);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.slow.Input");
            ports.Link("main.source.Output", "main.fast.Input");
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync(linkCapacity: 1);
        await appExecution.StartAsync();
        try
        {
            await fast.SecondReceived.Task.WaitAsync(TestDeadline);
            slow.Messages.ShouldBeEmpty();
            appExecution.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await appExecution.Completion.WaitAsync(TestDeadline);
        slow.Messages.ToArray().ShouldBe(messages);
        fast.Messages.ToArray().ShouldBe(messages);
    }

    [Fact]
    public async Task Standard_graph_routes_share_component_output_completion()
    {
        var source = new SequenceSource([Value(7)]);
        var sink = new RecordingSink();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.sink", sink);
        services.AddSingleton<LinkFactory>(ports => ports.Link("main.source.Output", "main.sink.Input"));
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync(linkCapacity: 2);
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
        sink.Messages.Single().Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(7);

        // This is deliberately a structural gate: a green facade over a second
        // output dispatcher does not satisfy the single-kernel requirement.
        var execution = appExecution;
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
        IFlowNode source = nativeSource
            ? new NativeSequenceSource([message, message, message])
            : new SequenceSource([message, message, message]);
        var ordinary = new RecordingSink();
        var fallback = new RecordingSink();
        var ignored = new RecordingSink();
        var selectedCalls = 0;
        var ignoredCalls = 0;
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.ordinary", ordinary);
        RegisterSink(services, "main.ignored", ignored);
        RegisterSink(services, "main.fallback", fallback);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.ordinary.Input", _ => ++selectedCalls % 2 == 0);
            ports.Link("main.source.Output", "main.ignored.Input", _ => { ignoredCalls++; return false; });
            ports.LinkFallback("main.source.Output", "main.fallback.Input");
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync(linkCapacity: 2);
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
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
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.earlier", earlierRoute);
        RegisterSink(services, "main.sink", sink);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.earlier.Input", _ => true);
            ports.Link("main.source.Output", "main.sink.Input", _ => throw failure);
        });
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        (await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline)))
            .ShouldBeSameAs(failure);
        (await native.ReceiveAsync().WaitAsync(TestDeadline)).ShouldBeSameAs(message);
        sink.Messages.ShouldBeEmpty();
        earlierRoute.Offers.ShouldBe(0);
        earlierRoute.Messages.ShouldBeEmpty();
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline));
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.DisposeAsync().AsTask()))
            .ShouldBeSameAs(failure);
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
        IFlowNode source = nativeSource ? new NativeSequenceSource([message]) : new SequenceSource([message]);
        var rejected = new ProbeSink(rejection);
        var sibling = new ProbeSink();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.rejected", rejected);
        RegisterSink(services, "main.sibling", sibling);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.rejected.Input", delivery: policy);
            ports.Link("main.source.Output", "main.sibling.Input");
        });
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        var error = await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline));
        error.Message.ShouldContain("main.source.Output");
        error.Message.ShouldContain("main.rejected.Input");
        var expectedOffers = policy == FlowDeliveryPolicy.Reliable && rejection == DataflowMessageStatus.Declined
            ? 2 : 1;
        rejected.Offers.ShouldBe(expectedOffers);
        rejected.InitialOffers.ShouldBe(1);
        rejected.Messages.ShouldBeEmpty();
        sibling.Messages.Single().ShouldBeSameAs(message);
        sibling.MessagesAtFault.ShouldBe(1);
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline));
        (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask()))
            .Flatten().InnerExceptions.ShouldContain(error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unselected_rejecting_route_is_not_offered_and_fallback_delivers(bool nativeSource)
    {
        var message = Value(13);
        IFlowNode source = nativeSource ? new NativeSequenceSource([message]) : new SequenceSource([message]);
        var rejected = new ProbeSink(DataflowMessageStatus.DecliningPermanently);
        var fallback = new ProbeSink();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.rejected", rejected);
        RegisterSink(services, "main.fallback", fallback);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.rejected.Input", _ => false, FlowDeliveryPolicy.RejectWhenFull);
            ports.LinkFallback("main.source.Output", "main.fallback.Input", FlowDeliveryPolicy.RejectWhenFull);
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
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
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.reserved", reserved);
        RegisterSink(services, "main.sibling", sibling);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.reserved.Input");
            ports.Link("main.source.Output", "main.sibling.Input");
        });
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        try
        {
            await reserved.ReservationAttempted.Task.WaitAsync(TestDeadline);
            await sibling.Accepted.Task.WaitAsync(TestDeadline);
            reserved.Reserved.ShouldBeTrue();
            source.Output.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { source.Output.Fault(failure); }
        (await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline)))
            .ShouldBeSameAs(failure);
        reserved.Released.ShouldBeTrue();
        reserved.Messages.ShouldBeEmpty();
        sibling.Messages.Single().ShouldBeSameAs(message);
        sibling.MessagesAtFault.ShouldBe(1);
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline));
        (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask()))
            .Flatten().InnerExceptions.ShouldContain(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reject_when_full_does_not_wait_for_native_target_capacity(bool nativeSource)
    {
        var full = new BufferedSink();
        full.Block.Post(Value(19)).ShouldBeTrue();
        IFlowNode source = nativeSource ? new NativeSequenceSource([Value(23)]) : new SequenceSource([Value(23)]);
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.full", full);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.full.Input",
                delivery: FlowDeliveryPolicy.RejectWhenFull);
        });
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        try
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline));
            error.Message.ShouldContain("main.full.Input");
            error.Message.ShouldContain("main.source.Output");
        }
        finally
        {
            full.Fault(new InvalidOperationException("test cleanup"));
            await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline));
            (await Should.ThrowAsync<InvalidOperationException>(() => provider.DisposeAsync().AsTask()))
                .Message.ShouldContain("main.full.Input");
        }
    }

    [Fact]
    public async Task Native_source_adapter_capacity_includes_the_active_delivery()
    {
        var source = new NativeSequenceSource([Value(29), Value(31), Value(37)]);
        var reserved = new ProbeSink(reserveUntilFault: true);
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.reserved", reserved);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.reserved.Input");
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync(linkCapacity: 1);
        await appExecution.StartAsync();
        try
        {
            await reserved.ReservationAttempted.Task.WaitAsync(TestDeadline);
            reserved.Reserved.ShouldBeTrue();
            source.PendingMessages.ShouldBe(2);
            source.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { reserved.Resume(); }
        await appExecution.Completion.WaitAsync(TestDeadline);
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
        IFlowNode source = nativeSource ? new NativeSequenceSource(messages) : new SequenceSource(messages);
        var targets = Enumerable.Range(0, targetCount).Select(_ => new ProbeSink()).ToArray();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        for (var index = 0; index < targets.Length; index++)
            RegisterSink(services, "main.sink" + index, targets[index]);
        services.AddSingleton<LinkFactory>(ports =>
        {
            for (var index = 0; index < targets.Length; index++)
                ports.Link("main.source.Output", "main.sink" + index + ".Input");
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);

        appExecution.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        foreach (var target in targets)
        {
            target.Messages.Select(message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([1, 2, 3]);
            target.Messages.Zip(messages).ShouldAllBe(pair => ReferenceEquals(pair.First, pair.Second));
            target.Offers.ShouldBe(messages.Length);
            target.InitialOffers.ShouldBe(messages.Length);
            target.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
    }

    private static void RegisterSource(IServiceCollection services, string address, SequenceSource source)
        => services.AddComponentFactory(address, _ => source)
            .AddOutputPort<SequenceSource>(address, "Output", node => node.Output)
            .AddEventPort<SequenceSource>(address, node => node.Events);

    private static void RegisterSource(IServiceCollection services, string address, IFlowNode source)
    {
        if (source is SequenceSource sequence)
            RegisterSource(services, address, sequence);
        else
            services.AddComponentFactory(address, _ => source)
                .AddOutputPort<NativeSequenceSource>(address, "Output", node => node.Output);
    }

    private static void RegisterSource(IServiceCollection services, string address, ManualRouteSource source)
        => services.AddComponentFactory(address, _ => source)
            .AddOutputPort<ManualRouteSource>(address, "Output", node => node.Output);

    private static void RegisterSink(IServiceCollection services, string address, BufferedSink sink)
        => services.AddComponentFactory(address, _ => sink)
            .AddInputPort<BufferedSink>(address, "Input", node => node.Block);

    private static void RegisterSink(IServiceCollection services, string address, ITargetBlock<FlowMessage> target)
        => services.AddComponentFactory(address, _ => new ProtocolComponent(target))
            .AddInputPort<ProtocolComponent>(address, "Input", node => node.Input);

    // Test component around a native protocol probe; no registration belongs in the component.
    private sealed class ProtocolComponent(ITargetBlock<FlowMessage> input) : IFlowNode
    {
        public ITargetBlock<FlowMessage> Input => input;
        public Task Completion => input.Completion;
        public void Complete() => input.Complete();
        public void Fault(Exception error) => input.Fault(error);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static void RegisterNode<T>(IServiceCollection services, string address, T node) where T : FlowNode
        => services.AddComponentFactory(address, _ => node)
            .AddInputPort<T>(address, "Input", value => value.Input)
            .AddOutputPort<T>(address, "Output", value => value.Output)
            .AddEventPort<T>(address, value => value.Events);

    private static void RegisterSink(IServiceCollection services, string address, RecordingSink sink)
        => services.AddComponentFactory(address, _ => sink)
            .AddInputPort<RecordingSink>(address, "Input", node => node.Input);

    private static FlowMessage Value(int value) => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public async Task Graph_fault_drains_completing_native_source_without_business_delivery(
        int capacity, bool internalFault)
    {
        var source = new NativeSequenceSource(Enumerable.Range(1, capacity + 3).Select(Value).ToArray());
        var held = new HeldTarget();
        var sibling = new ProbeSink();
        var failure = new InvalidOperationException("graph failed during native drain");
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.held", held);
        RegisterSink(services, "main.sibling", sibling);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.source.Output", "main.held.Input");
            ports.Link("main.source.Output", "main.sibling.Input");
        });
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync(linkCapacity: capacity);
        await appExecution.StartAsync();
        try
        {
            await held.ReservationReady.Task.WaitAsync(TestDeadline);
            await sibling.Accepted.Task.WaitAsync(TestDeadline);
            held.Reserved.ShouldBeTrue();
            source.PendingMessages.ShouldBeGreaterThan(0);
            source.Completion.IsCompleted.ShouldBeFalse();
            if (internalFault)
            {
                sibling.Fault(failure);
                (await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline))).ShouldBeSameAs(failure);
            }
            else
            {
                appExecution.Fault(failure);
                (await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline)))
                    .ShouldBeSameAs(failure);
            }
            appExecution.Fault(new InvalidOperationException("later failure"));
            held.Released.ShouldBeTrue();
            sibling.Messages.Select(message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([1]);
            source.PendingMessages.ShouldBe(0);
            source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline));
            source.DisposeCalls.ShouldBe(0);
            (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask()))
                .Flatten().InnerExceptions.ShouldContain(failure);
            source.DisposeCalls.ShouldBe(1);
        }
        finally
        {
            // Permit cleanup even when a regression leaves the native tail stranded.
            if (!appExecution.Completion.IsCompleted) appExecution.Fault(failure);
            source.DrainForCleanup();
            try { await appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline); }
            catch (AggregateException) { }
        }
    }

    private sealed class HeldTarget : ITargetBlock<FlowMessage>
    {
        private readonly TaskCompletionSource _completion = Signal();
        private ISourceBlock<FlowMessage>? _source;
        private DataflowMessageHeader _header;
        private int _released;
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
    }

    private sealed class BufferedSink : IFlowNode
    {
        public BufferBlock<FlowMessage> Block { get; } = new(new DataflowBlockOptions { BoundedCapacity = 1 });
        public Task Completion => Block.Completion;
        public void Complete() => Block.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)Block).Fault(error);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }

    // A synchronous protocol probe records admission before a subsequent graph fault
    // can discard queued input. It is not an alternate production delivery mechanism.
    private sealed class ProbeSink(DataflowMessageStatus? rejection = null, bool reserveUntilFault = false,
        Exception? admissionFailure = null)
        : ITargetBlock<FlowMessage>
    {
        private readonly TaskCompletionSource _completion = Signal();
        private ISourceBlock<FlowMessage>? _source;
        private DataflowMessageHeader _header;
        private bool _resumed;
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
    }

    private sealed class NativeSequenceSource(FlowMessage[] messages) : IFlowSource
    {
        private readonly BufferBlock<FlowMessage> _output = new(new DataflowBlockOptions { BoundedCapacity = 8 });
        public int PendingMessages => _output.Count;
        public int DisposeCalls { get; private set; }
        public void DrainForCleanup() => _output.TryReceiveAll(out _);
        public ISourceBlock<FlowMessage> Output => _output;
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
            => await EmitAsync(message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(_dependency.Double(message.Value!.Json.Deserialize<int>()), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
    }

    // A sink needs only its actual input and block lifetime. No base class, DI, descriptor,
    // creator, materializer, output/event placeholder or engine integration is required.
    private sealed class RecordingSink : IFlowNode
    {
        private readonly ActionBlock<FlowMessage> _input;
        public ITargetBlock<FlowMessage> Input => _input;
        public RecordingSink(Task? release = null)
        {
            _input = new ActionBlock<FlowMessage>(async message =>
            {
                if (release is not null) await release;
                Messages.Enqueue(message);
                FirstReceived.TrySetResult();
                if (Messages.Count == 2) SecondReceived.TrySetResult();
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = 1 });
        }
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public TaskCompletionSource FirstReceived { get; } = Signal();
        public TaskCompletionSource SecondReceived { get; } = Signal();
        public int DisposeCalls { get; private set; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_input).Fault(exception);
        public async ValueTask DisposeAsync() { DisposeCalls++; Complete(); await Completion; }
    }
}
