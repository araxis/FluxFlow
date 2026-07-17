using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationPortRuntimeTests
{
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("Main", "Processor", "Input");
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("Main", "Source", "Output");

    [Fact]
    public async Task Direct_send_reports_unavailable_full_and_completed_without_waiting()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input, capacity: 1)
            .Build();
        var first = Message("first");

        (await runtime.SendAsync(Input, first)).Status.ShouldBe(PortSendStatus.Unavailable);

        var target = new BufferBlock<FlowMessage<string>>(new DataflowBlockOptions
        {
            BoundedCapacity = 1
        });
        target.Post(Message("occupied")).ShouldBeTrue();
        await using var attachment = await runtime.AttachInputAsync(Input, target);

        (await runtime.SendAsync(Input, first)).Status.ShouldBe(PortSendStatus.Accepted);
        await EventuallyAsync(async () =>
            (await runtime.SendAsync(Input, Message("overflow"))).Status == PortSendStatus.Full);

        runtime.Complete();
        (await runtime.SendAsync(Input, Message("late"))).Status.ShouldBe(PortSendStatus.Completed);
        (await target.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("occupied");
        (await target.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("first");
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Input_revision_swap_finishes_claimed_work_and_moves_queued_work_to_new_target()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input, capacity: 3)
            .Build();
        var oldTarget = new PostponedTarget<FlowMessage<string>>();
        var oldAttachment = await runtime.AttachInputAsync(Input, oldTarget);

        (await runtime.SendAsync(Input, Message("claimed"))).IsAccepted.ShouldBeTrue();
        await oldTarget.Offered.WaitAsync(TimeSpan.FromSeconds(5));
        (await runtime.SendAsync(Input, Message("queued"))).IsAccepted.ShouldBeTrue();

        var newTarget = new BufferBlock<FlowMessage<string>>(new DataflowBlockOptions
        {
            BoundedCapacity = 4
        });
        var swap = runtime.AttachInputAsync(Input, newTarget).AsTask();
        await Task.Delay(30);
        swap.IsCompleted.ShouldBeFalse();

        oldTarget.AcceptPostponed();
        var newAttachment = await swap.WaitAsync(TimeSpan.FromSeconds(5));
        oldTarget.Accepted.Single().Payload.ShouldBe("claimed");
        (await newTarget.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("queued");

        await oldAttachment.DisposeAsync();
        (await runtime.SendAsync(Input, Message("after-swap"))).IsAccepted.ShouldBeTrue();
        (await newTarget.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("after-swap");
        await newAttachment.DisposeAsync();
    }

    [Fact]
    public async Task Rejected_revision_keeps_claimed_message_for_next_attachment()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .Build();
        var rejected = new RejectingTarget<FlowMessage<string>>();
        await using var rejectedAttachment = await runtime.AttachInputAsync(Input, rejected);

        (await runtime.SendAsync(Input, Message("retry"))).IsAccepted.ShouldBeTrue();
        var failure = await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        failure.Reason.ShouldBe(ApplicationPortRejectionReason.TargetRejected);

        var replacement = new BufferBlock<FlowMessage<string>>();
        await using var replacementAttachment = await runtime.AttachInputAsync(Input, replacement);
        (await replacement.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("retry");
    }

    [Fact]
    public async Task Completion_drops_retained_work_with_a_terminal_rejection_when_no_target_remains()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .Build();
        var rejected = new RejectingTarget<FlowMessage<string>>();
        await using var attachment = await runtime.AttachInputAsync(Input, rejected);
        var retained = Message("retained");

        (await runtime.SendAsync(Input, retained)).IsAccepted.ShouldBeTrue();
        (await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Reason.ShouldBe(ApplicationPortRejectionReason.TargetRejected);

        runtime.Complete();

        var terminal = await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        terminal.Reason.ShouldBe(ApplicationPortRejectionReason.Completed);
        terminal.TraceId.ShouldBe(retained.TraceId);
        terminal.MessageId.ShouldBe(retained.MessageId);
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pending_input_swap_cannot_attach_a_target_after_runtime_disposal()
    {
        var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .Build();
        var oldTarget = new PostponedTarget<FlowMessage<string>>();
        await using var oldAttachment = await runtime.AttachInputAsync(Input, oldTarget);
        (await runtime.SendAsync(Input, Message("claimed"))).IsAccepted.ShouldBeTrue();
        await oldTarget.Offered.WaitAsync(TimeSpan.FromSeconds(5));

        var replacement = new BufferBlock<FlowMessage<string>>();
        var swap = runtime.AttachInputAsync(Input, replacement).AsTask();
        await Task.Delay(30);
        swap.IsCompleted.ShouldBeFalse();

        await runtime.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(async () => await swap);
    }

    [Fact]
    public async Task Direct_receive_observes_output_without_stealing_workflow_delivery()
    {
        var sinkAddress = ApplicationAddress.WorkflowPort("Main", "Sink", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(Output)
            .AddInput<string>(sinkAddress)
            .Build();
        var sink = new BufferBlock<FlowMessage<string>>();
        await using var sinkAttachment = await runtime.AttachInputAsync(sinkAddress, sink);
        using var route = runtime.Connect(CompileLinks(
            "\"Sink.Input\"",
            sinkNames: ["Sink"]).Single());
        var source = new BufferBlock<FlowMessage<string>>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);

        var receive = runtime.ReceiveAsync<string>(Output, TimeSpan.FromSeconds(5));
        source.Post(Message("broadcast")).ShouldBeTrue();

        (await receive).Message!.Payload.ShouldBe("broadcast");
        (await sink.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("broadcast");
    }

    [Fact]
    public async Task Source_completion_does_not_complete_stable_output_or_observation()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(Output)
            .Build();
        var firstSource = new BufferBlock<FlowMessage<string>>();
        using var firstAttachment = runtime.AttachOutput(Output, firstSource);
        var observed = await runtime.ObserveAsync<string>(Output, capacity: 4);
        observed.Status.ShouldBe(PortObserveStatus.Started);
        await using var observation = observed.Observation!;

        firstSource.Post(Message("first")).ShouldBeTrue();
        (await observation.Messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("first");
        firstSource.Complete();
        await firstSource.Completion;

        var secondSource = new BufferBlock<FlowMessage<string>>();
        using var secondAttachment = runtime.AttachOutput(Output, secondSource);
        secondSource.Post(Message("second")).ShouldBeTrue();
        (await observation.Messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("second");
        observation.Completion.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Receive_and_observe_report_unavailable_timeout_and_completion_as_results()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(Output)
            .Build();

        (await runtime.ReceiveAsync<string>(Output)).Status.ShouldBe(PortReceiveStatus.Unavailable);
        (await runtime.ObserveAsync<string>(Output)).Status.ShouldBe(PortObserveStatus.Unavailable);

        var source = new BufferBlock<FlowMessage<string>>();
        using var attachment = runtime.AttachOutput(Output, source);
        (await runtime.ReceiveAsync<string>(Output, TimeSpan.FromMilliseconds(30)))
            .Status.ShouldBe(PortReceiveStatus.TimedOut);

        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        (await runtime.ReceiveAsync<string>(Output)).Status.ShouldBe(PortReceiveStatus.Completed);
        (await runtime.ObserveAsync<string>(Output)).Status.ShouldBe(PortObserveStatus.Completed);
    }

    [Fact]
    public async Task Conditional_failure_isolated_from_healthy_sibling()
    {
        var failingAddress = ApplicationAddress.WorkflowPort("Main", "Failing", "Input");
        var healthyAddress = ApplicationAddress.WorkflowPort("Main", "Healthy", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(Output)
            .AddInput<string>(failingAddress)
            .AddInput<string>(healthyAddress)
            .Build();
        var failing = new BufferBlock<FlowMessage<string>>();
        var healthy = new BufferBlock<FlowMessage<string>>();
        await using var failingAttachment = await runtime.AttachInputAsync(failingAddress, failing);
        await using var healthyAttachment = await runtime.AttachInputAsync(healthyAddress, healthy);
        var links = CompileLinks(
            "[{ \"Port\": \"Failing.Input\", \"Condition\": \"fail\" }, \"Healthy.Input\"]",
            sinkNames: ["Failing", "Healthy"],
            expressionEngine: new FailingExpressionEngine());
        using var firstRoute = runtime.Connect(links[0]);
        using var secondRoute = runtime.Connect(links[1]);
        var source = new BufferBlock<FlowMessage<string>>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);

        source.Post(Message("value")).ShouldBeTrue();

        (await healthy.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("value");
        (await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Reason.ShouldBe(ApplicationPortRejectionReason.ConditionFailed);
        failing.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Full_target_does_not_block_healthy_fanout_sibling()
    {
        var slowAddress = ApplicationAddress.WorkflowPort("Main", "Slow", "Input");
        var healthyAddress = ApplicationAddress.WorkflowPort("Main", "Healthy", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(Output)
            .AddInput<string>(slowAddress, capacity: 1)
            .AddInput<string>(healthyAddress)
            .Build();
        var slow = new PostponedTarget<FlowMessage<string>>();
        var healthy = new BufferBlock<FlowMessage<string>>();
        await using var slowAttachment = await runtime.AttachInputAsync(slowAddress, slow);
        await using var healthyAttachment = await runtime.AttachInputAsync(healthyAddress, healthy);
        var links = CompileLinks(
            "[\"Slow.Input\", \"Healthy.Input\"]",
            sinkNames: ["Slow", "Healthy"]);
        using var firstRoute = runtime.Connect(links[0]);
        using var secondRoute = runtime.Connect(links[1]);
        var source = new BufferBlock<FlowMessage<string>>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);

        source.Post(Message("one")).ShouldBeTrue();
        await slow.Offered.WaitAsync(TimeSpan.FromSeconds(5));
        source.Post(Message("two")).ShouldBeTrue();

        (await healthy.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("one");
        (await healthy.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("two");
        var rejection = await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        rejection.Port.ShouldBe(slowAddress);
        rejection.Reason.ShouldBe(ApplicationPortRejectionReason.Full);
        slow.AcceptPostponed();
    }

    [Fact]
    public async Task Slow_observation_overflow_does_not_block_sibling_observation()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(Output)
            .Build();
        var source = new BufferBlock<FlowMessage<string>>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);
        var slowResult = await runtime.ObserveAsync<string>(Output, capacity: 1);
        var fastResult = await runtime.ObserveAsync<string>(Output, capacity: 4);
        await using var slow = slowResult.Observation!;
        await using var fast = fastResult.Observation!;

        source.Post(Message("one")).ShouldBeTrue();
        source.Post(Message("two")).ShouldBeTrue();

        (await fast.Messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("one");
        (await fast.Messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldBe("two");
        (await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Reason.ShouldBe(ApplicationPortRejectionReason.ObservationOverflowed);
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await slow.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Request_reply_registers_before_send_and_matches_trace_id()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .AddOutput<string>(Output)
            .Build();
        var responses = new BufferBlock<FlowMessage<string>>();
        using var outputAttachment = runtime.AttachOutput(Output, responses);
        var processor = new ActionBlock<FlowMessage<string>>(request =>
        {
            responses.Post(FlowMessage.Create("wrong"));
            responses.Post(request.With("right"));
        });
        await using var inputAttachment = await runtime.AttachInputAsync(Input, processor);
        var request = Message("request");

        var result = await runtime.SendAndReceiveAsync<string, string>(
            Input,
            Output,
            request,
            TimeSpan.FromSeconds(5));

        result.Status.ShouldBe(PortRequestStatus.Received);
        result.Response!.Payload.ShouldBe("right");
        result.Response.TraceId.ShouldBe(request.TraceId);
    }

    [Fact]
    public async Task Builder_and_runtime_reject_invalid_address_direction_and_type_use()
    {
        Should.Throw<ArgumentException>(() => new ApplicationPortRuntimeBuilder()
            .AddInput<string>(ApplicationAddress.SystemEvents));
        Should.Throw<ArgumentException>(() => new ApplicationPortRuntimeBuilder()
            .AddOutput<string>(ApplicationAddress.Resource("Broker")));
        Should.Throw<InvalidOperationException>(() => new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .AddOutput<string>(Input));

        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .AddOutput<string>(Output)
            .Build();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await runtime.SendAsync<int>(Input, FlowMessage.Create(1)));
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await runtime.ReceiveAsync<string>(Input));
    }

    [Fact]
    public async Task Pre_canceled_direct_operations_remain_canceled()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput<string>(Input)
            .AddOutput<string>(Output)
            .Build();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.SendAsync(Input, Message("value"), canceled.Token));
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.ReceiveAsync<string>(Output, cancellationToken: canceled.Token));
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.ObserveAsync<string>(Output, cancellationToken: canceled.Token));
    }

    private static FlowMessage<string> Message(string payload)
        => FlowMessage.Create(payload);

    private static IReadOnlyList<CompiledApplicationLink> CompileLinks(
        string outputDeclaration,
        IReadOnlyList<string> sinkNames,
        IFlowExpressionEngine? expressionEngine = null)
    {
        var sinks = string.Join(",", sinkNames.Select(name => $"\"{name}\": {{ \"Type\": \"sink\" }}"));
        var definition = ApplicationDefinitionJson.Deserialize(
            $$"""
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": { "Type": "source", "Output": {{outputDeclaration}} },
                  {{sinks}}
                }
              }
            }
            """);
        var registry = new CompositionNodeRegistry()
            .Register(
                "source",
                UnusedFactory,
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                "sink",
                UnusedFactory,
                inputs: [CompositionPorts.Metadata<string>("Input")]);
        var result = new ApplicationLinkCompiler(registry, expressionEngine).Compile(definition);
        result.IsValid.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        return result.Links;
    }

    private static ValueTask<ComposedNode> UnusedFactory(CompositionNodeFactoryContext _)
        => throw new InvalidOperationException("Link compilation must not activate node factories.");

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            if (await condition())
                return;
            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not reached before the test timeout.");
    }

    private sealed class FailingExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "test";

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => throw new InvalidOperationException("Compiled expressions are required.");

        public IFlowCompiledExpression<T> Compile<T>(string expression)
            => (IFlowCompiledExpression<T>)(object)new FailingExpression();
    }

    private sealed class FailingExpression : IFlowCompiledExpression<bool>
    {
        public bool Evaluate(FlowMapContext context)
            => throw new InvalidOperationException("Condition failed.");
    }

    private sealed class PostponedTarget<T> : ITargetBlock<T>
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _offered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ISourceBlock<T>? _source;
        private DataflowMessageHeader _header;

        public List<T> Accepted { get; } = [];

        public Task Offered => _offered.Task;

        public Task Completion => _completion.Task;

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
        {
            lock (_gate)
            {
                if (_completion.Task.IsCompleted)
                    return DataflowMessageStatus.DecliningPermanently;
                if (_source is not null)
                    return DataflowMessageStatus.Postponed;
                if (source is null)
                    return DataflowMessageStatus.Declined;

                _header = messageHeader;
                _source = source;
                _offered.TrySetResult();
                return DataflowMessageStatus.Postponed;
            }
        }

        public void AcceptPostponed()
        {
            ISourceBlock<T> source;
            DataflowMessageHeader header;
            lock (_gate)
            {
                source = _source.ShouldNotBeNull();
                header = _header;
                _source = null;
            }

            var value = source.ConsumeMessage(header, this, out var consumed);
            consumed.ShouldBeTrue();
            Accepted.Add(value!);
        }

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class RejectingTarget<T> : ITargetBlock<T>
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
            => DataflowMessageStatus.DecliningPermanently;

        public void Complete() => _completion.TrySetResult();

        public void Fault(Exception exception) => _completion.TrySetException(exception);
    }
}
