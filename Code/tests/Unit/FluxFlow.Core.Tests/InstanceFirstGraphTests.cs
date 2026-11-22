using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using FluxFlow.Application.Execution;
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
        RegisterNode(services, "main.double", transformInstance, node => node.Input);
        RegisterSink(services, "reports.first", left);
        RegisterSink(services, "reports.second", right);
        FlowLink? link = null;
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.double.Input"));
            link = ports.Link(PortAddress.Parse("main.double.Output"), PortAddress.Parse("reports.first.Input"), message => !message.IsError);
            ports.Link(PortAddress.Parse("main.double.Output"), PortAddress.Parse("reports.second.Input"), message => message.Value.Json.GetInt32() > 0);
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
        transformInstance.ConstructorCalls.ShouldBe(1);
        dependency.Calls.ShouldBe(2);
        dependency.Disposed.ShouldBeFalse();
        provider.GetWorkflowServices("reports").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("reports.second.Input").ShouldBeSameAs(right.Input);
        provider.GetWorkflowServices("main").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.double.Output").ShouldBeSameAs(transformInstance.Output);
        link.ShouldNotBeNull().InputAddress.Value.ShouldBe("reports.first.Input");
        link.Input.ShouldBeSameAs(left.Input);
        link.Output.ShouldBeSameAs(transformInstance.Output);
        link.OutputAddress.Value.ShouldBe("main.double.Output");
        left.Messages.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([6, 10]);
        right.Messages.Count.ShouldBe(2);
        left.Messages.Zip(right.Messages).All(pair => ReferenceEquals(pair.First, pair.Second)).ShouldBeTrue();
        left.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        right.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await appExecution.DisposeAsync();
        left.DisposeCalls.ShouldBe(1);
        right.DisposeCalls.ShouldBe(1);
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
            ports.Link(PortAddress.Parse("main.early.Output"), PortAddress.Parse("main.sink.Input"));
            ports.Link(PortAddress.Parse("main.late.Output"), PortAddress.Parse("main.sink.Input"));
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
        finally
        {
            release.TrySetResult();
        }

        await appExecution.Completion.WaitAsync(TestDeadline);
        sink.Messages.ToArray().ShouldBe([first, second]);
        sink.Messages.Last().ShouldBeSameAs(second);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Unmatched_messages_are_consumed_and_complete_without_fault(bool nativeSource, bool connectRoute)
    {
        var messages = Enumerable.Range(0, 3).Select(Value).ToArray();
        IFlowNode source = nativeSource ? new NativeSequenceSource(messages) : new SequenceSource(messages);
        var sink = new RecordingSink();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        var evaluations = 0;
        if (connectRoute)
        {
            RegisterSink(services, "main.sink", sink);
            services.AddSingleton<LinkFactory>(ports => ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.sink.Input"), _ =>
            {
                evaluations++;
                return false;
            }));
        }

        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
        evaluations.ShouldBe(connectRoute ? messages.Length : 0);
        sink.Messages.ShouldBeEmpty();
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        appExecution.Completion.IsCompletedSuccessfully.ShouldBeTrue();
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
        first.DisposeCalls.ShouldBe(1);
        second.DisposeCalls.ShouldBe(1);
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
        RegisterNode(services, "main.one", one, node => node.Input);
        RegisterNode(services, "main.two", new Relay(), node => node.Input);
        services.AddSingleton<LinkFactory>(ports =>
        {
            Should.Throw<KeyNotFoundException>(() => ports.Link(PortAddress.Parse("main.one.Output"), PortAddress.Parse("foreign.sink.Input")));
            var foreignRoute = new FlowLink(PortAddress.Parse("main.one.Output"), one.Output, PortAddress.Parse("main.two.Input"), foreign.Input, null);
            Should.Throw<ArgumentException>(() => ports.GetRequiredService<FlowApplication>().AddLink(foreignRoute));
            ports.Link(PortAddress.Parse("main.one.Output"), PortAddress.Parse("main.two.Input"));
            ports.Link(PortAddress.Parse("main.two.Output"), PortAddress.Parse("main.one.Input"));
        });
        await using var provider = services.BuildServiceProvider();
        (await Should.ThrowAsync<InvalidOperationException>(() => provider.BuildApplicationAsync())).Message.ShouldContain("cycle");
        source.Runs.ShouldBe(0);
        var validServices = new ServiceCollection();
        RegisterNode(validServices, "valid.relay", new Relay(), node => node.Input);
        RegisterSink(validServices, "valid.sink", new RecordingSink());
        validServices.AddSingleton<LinkFactory>(ports => ports.Link(PortAddress.Parse("valid.relay.Output"), PortAddress.Parse("valid.sink.Input")));
        await using var validProvider = validServices.BuildServiceProvider();
        await using var validExecution = await validProvider.BuildApplicationAsync();
        await validExecution.StartAsync();
        Should.Throw<InvalidOperationException>(() => validProvider.Link(PortAddress.Parse("valid.relay.Output"), PortAddress.Parse("valid.sink.Input")));
    }

    [Fact]
    public async Task Errors_forward_the_same_envelope_without_processing()
    {
        var failure = FlowMessage.Create(new FlowError("test.failure", "expected", "test", false));
        var relay = new Relay();
        var sink = new RecordingSink();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", new SequenceSource([failure]));
        RegisterNode(services, "main.relay", relay, node => node.Input);
        RegisterSink(services, "main.sink", sink);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.relay.Input"));
            ports.Link(PortAddress.Parse("main.relay.Output"), PortAddress.Parse("main.sink.Input"), message => message.IsError);
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
        var messages = new[]
        {
            Value(1),
            Value(2)
        };
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", new SequenceSource(messages));
        RegisterSink(services, "main.slow", slow);
        RegisterSink(services, "main.fast", fast);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.slow.Input"));
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.fast.Input"));
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        try
        {
            await fast.SecondReceived.Task.WaitAsync(TestDeadline);
            slow.Messages.ShouldBeEmpty();
            appExecution.Completion.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
        }

        await appExecution.Completion.WaitAsync(TestDeadline);
        slow.Messages.First().ShouldBeSameAs(messages[0]);
        slow.Messages.Count.ShouldBeLessThanOrEqualTo(messages.Length);
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
        services.AddSingleton<LinkFactory>(ports => ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.sink.Input")));
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
        sink.Messages.Single().Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(7);
        source.Output.ShouldBeOfType<BroadcastBlock<FlowMessage>>();
        typeof(FlowApplication).Assembly.GetType("FluxFlow.Application.Execution.FlowRouter").ShouldBeNull();
        typeof(FlowApplication).Assembly.GetType("FluxFlow.Core.Nodes.FlowOutput`1").ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_message_instances_re_evaluate_routes_for_every_emission(bool nativeSource)
    {
        var message = Value(5);
        IFlowNode source = nativeSource ? new NativeSequenceSource([message, message, message]) : new SequenceSource([message, message, message]);
        var ordinary = new RecordingSink();
        var ignored = new RecordingSink();
        var selectedCalls = 0;
        var ignoredCalls = 0;
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.ordinary", ordinary);
        RegisterSink(services, "main.ignored", ignored);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.ordinary.Input"), _ => ++selectedCalls % 2 == 0);
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.ignored.Input"), _ =>
            {
                ignoredCalls++;
                return false;
            });
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
        selectedCalls.ShouldBe(3);
        ignoredCalls.ShouldBe(3);
        ordinary.Messages.Single().ShouldBeSameAs(message);
        ignored.Messages.ShouldBeEmpty();
        ignored.Completion.IsCompletedSuccessfully.ShouldBeTrue();
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
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.earlier.Input"), _ => true);
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.sink.Input"), _ => throw failure);
        });
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        (await Should.ThrowAsync<InvalidOperationException>(() => appExecution.Completion.WaitAsync(TestDeadline))).ShouldBeSameAs(failure);
        (await native.ReceiveAsync().WaitAsync(TestDeadline)).ShouldBeSameAs(message);
        sink.Messages.ShouldBeEmpty();
        earlierRoute.Offers.ShouldBe(1);
        earlierRoute.Messages.Single().ShouldBeSameAs(message);
        await Should.ThrowAsync<AggregateException>(() => appExecution.DisposeAsync().AsTask().WaitAsync(TestDeadline));
        (await Should.ThrowAsync<AggregateException>(() => provider.DisposeAsync().AsTask())).GetBaseException().ShouldBeSameAs(failure);
    }

    [Theory]
    [InlineData(false, DataflowMessageStatus.Declined)]
    [InlineData(false, DataflowMessageStatus.DecliningPermanently)]
    [InlineData(true, DataflowMessageStatus.Declined)]
    [InlineData(true, DataflowMessageStatus.DecliningPermanently)]
    public async Task Declining_target_does_not_fault_publisher_or_prevent_sibling_delivery(bool nativeSource, DataflowMessageStatus rejection)
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
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.rejected.Input"));
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.sibling.Input"));
        });
        await using var provider = services.BuildServiceProvider();
        var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
        rejected.Offers.ShouldBe(1);
        rejected.Messages.ShouldBeEmpty();
        sibling.Messages.Single().ShouldBeSameAs(message);
        await appExecution.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unselected_rejecting_route_is_not_offered(bool nativeSource)
    {
        var message = Value(13);
        IFlowNode source = nativeSource ? new NativeSequenceSource([message]) : new SequenceSource([message]);
        var rejected = new ProbeSink(DataflowMessageStatus.DecliningPermanently);
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        RegisterSink(services, "main.rejected", rejected);
        services.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.rejected.Input"), _ => false);
        });
        await using var provider = services.BuildServiceProvider();
        await using var appExecution = await provider.BuildApplicationAsync();
        await appExecution.StartAsync();
        await appExecution.Completion.WaitAsync(TestDeadline);
        rejected.Offers.ShouldBe(0);
        rejected.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public async Task Graph_routes_preserve_native_immediate_admission(bool nativeSource, int targetCount)
    {
        var messages = new[]
        {
            Value(1),
            Value(2),
            Value(3)
        };
        IFlowNode source = nativeSource ? new NativeSequenceSource(messages) : new SequenceSource(messages);
        var targets = Enumerable.Range(0, targetCount).Select(_ => new ProbeSink()).ToArray();
        var services = new ServiceCollection();
        RegisterSource(services, "main.source", source);
        for (var index = 0; index < targets.Length; index++)
            RegisterSink(services, "main.sink" + index, targets[index]);
        services.AddSingleton<LinkFactory>(ports =>
        {
            for (var index = 0; index < targets.Length; index++)
                ports.Link(PortAddress.Parse("main.source.Output"), PortAddress.Parse("main.sink" + index + ".Input"));
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
            target.InitialOffers.ShouldBe(0);
            target.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
    }

    private static void RegisterSource(IServiceCollection services, string address, SequenceSource source) => services.AddComponentFactory(ComponentAddress.Parse(address), _ => source).AddOutputPort<SequenceSource>(ComponentAddress.Parse(address), "Output", node => node.Output).AddEventPort<SequenceSource>(ComponentAddress.Parse(address), node => node.Events);
    private static void RegisterSource(IServiceCollection services, string address, IFlowNode source)
    {
        if (source is SequenceSource sequence)
            RegisterSource(services, address, sequence);
        else
            services.AddComponentFactory(ComponentAddress.Parse(address), _ => source).AddOutputPort<NativeSequenceSource>(ComponentAddress.Parse(address), "Output", node => node.Output);
    }

    private static void RegisterSource(IServiceCollection services, string address, ManualRouteSource source) => services.AddComponentFactory(ComponentAddress.Parse(address), _ => source).AddOutputPort<ManualRouteSource>(ComponentAddress.Parse(address), "Output", node => node.Output);
    private static void RegisterSink(IServiceCollection services, string address, BufferedSink sink) => services.AddComponentFactory(ComponentAddress.Parse(address), _ => sink).AddInputPort<BufferedSink>(ComponentAddress.Parse(address), "Input", node => node.Block);
    private static void RegisterSink(IServiceCollection services, string address, ITargetBlock<FlowMessage> target) => services.AddComponentFactory(ComponentAddress.Parse(address), _ => new ProtocolComponent(target)).AddInputPort<ProtocolComponent>(ComponentAddress.Parse(address), "Input", node => node.Input);
    // Test component around a native protocol probe; no registration belongs in the component.
    private sealed class ProtocolComponent(ITargetBlock<FlowMessage> input) : IFlowNode
    {
        public ITargetBlock<FlowMessage> Input => input;
        public Task Completion => input.Completion;

        public void Complete() => input.Complete();
        public void Fault(Exception error) => input.Fault(error);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static void RegisterNode<T>(IServiceCollection services, string address, T node, Func<T, ITargetBlock<FlowMessage>> input)
        where T : FlowNode => services.AddComponentFactory(ComponentAddress.Parse(address), _ => node).AddInputPort<T>(ComponentAddress.Parse(address), "Input", input).AddOutputPort<T>(ComponentAddress.Parse(address), "Output", value => value.Output).AddEventPort<T>(ComponentAddress.Parse(address), value => value.Events);
    private static void RegisterSink(IServiceCollection services, string address, RecordingSink sink) => services.AddComponentFactory(ComponentAddress.Parse(address), _ => sink).AddInputPort<RecordingSink>(ComponentAddress.Parse(address), "Input", node => node.Input);
    private static FlowMessage Value(int value) => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    private sealed class BufferedSink : IFlowNode
    {
        public BufferBlock<FlowMessage> Block { get; } = new(new DataflowBlockOptions { BoundedCapacity = 1 });
        public Task Completion => Block.Completion;

        public void Complete() => Block.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)Block).Fault(error);
        public async ValueTask DisposeAsync()
        {
            Complete();
            await Completion;
        }
    }

    // A synchronous protocol probe records admission before a subsequent graph fault
    // can discard queued input. It is not an alternate production delivery mechanism.
    private sealed class ProbeSink(DataflowMessageStatus? rejection = null) : ITargetBlock<FlowMessage>
    {
        private readonly TaskCompletionSource _completion = Signal();
        public Task Completion => _completion.Task;
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int Offers { get; private set; }
        public int InitialOffers { get; private set; }
        public TaskCompletionSource Accepted { get; } = Signal();

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader header, FlowMessage value, ISourceBlock<FlowMessage>? source, bool consumeToAccept)
        {
            Offers++;
            if (source is null)
                InitialOffers++;
            if (rejection is { } status)
                return status;
            if (consumeToAccept)
            {
                value = source!.ConsumeMessage(header, this, out var consumed)!;
                if (!consumed)
                    return DataflowMessageStatus.NotAvailable;
            }

            Messages.Enqueue(value);
            Accepted.TrySetResult();
            return DataflowMessageStatus.Accepted;
        }

        public void Complete() => _completion.TrySetResult();
        public void Fault(Exception error) => _completion.TrySetException(error);
    }

    private sealed class NativeSequenceSource(FlowMessage[] messages) : IFlowSource
    {
        private readonly BroadcastBlock<FlowMessage> _output = new(static message => message);
        public int DisposeCalls { get; private set; }
        public ISourceBlock<FlowMessage> Output => _output;
        public Task Completion => _output.Completion;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var message in messages)
                if (!_output.Post(message))
                    throw new InvalidOperationException("Native source capacity exceeded.");
            _output.Complete();
            return Task.CompletedTask;
        }

        public void Complete() => _output.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_output).Fault(error);
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Complete();
            try
            {
                await Completion;
            }
            catch
            {
            }
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class SequenceSource : FlowNode, IFlowSource
    {
        public Task ProductionCompletion => Lifetime.ProductionCompletion;

        public void StopProduction() => Lifetime.StopProduction();
        public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
        public SequenceSource(FlowMessage[] messages, Task? ready = null)
        {
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, produce: RunAsync);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            this.messages = messages;
            this.ready = ready;
            Lifetime.Observe();
        }

        private readonly Task? ready;
        private readonly FlowMessage[] messages;
        public int Runs { get; private set; }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            Runs++;
            if (ready is not null)
                await ready.WaitAsync(cancellationToken);
            foreach (var message in messages)
                await EmitAsync(message, cancellationToken);
        }
    }

    private sealed class Relay : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public Relay()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        public int ProcessCalls { get; private set; }

        private async Task ProcessAsync(FlowMessage message)
        {
            ProcessCalls++;
            await EmitAsync(message);
        }
    }

    private sealed class BorrowedDependency : IDisposable
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }

        public int Double(int value)
        {
            Calls++;
            return value * 2;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class Multiply : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        private readonly BorrowedDependency _dependency;
        public Multiply(BorrowedDependency dependency)
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            _dependency = dependency;
            ConstructorCalls++;
            Lifetime.Observe();
        }

        public int ConstructorCalls { get; }

        private async Task ProcessAsync(FlowMessage message) => await EmitAsync(message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(_dependency.Double(message.Value!.Json.Deserialize<int>()), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
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
                if (release is not null)
                    await release;
                Messages.Enqueue(message);
                FirstReceived.TrySetResult();
                if (Messages.Count == 2)
                    SecondReceived.TrySetResult();
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = release is null ? DataflowBlockOptions.Unbounded : 1 });
        }

        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public TaskCompletionSource FirstReceived { get; } = Signal();
        public TaskCompletionSource SecondReceived { get; } = Signal();
        public int DisposeCalls { get; private set; }
        public Task Completion => _input.Completion;

        public void Complete() => _input.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_input).Fault(exception);
        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            Complete();
            await Completion;
        }
    }
}
