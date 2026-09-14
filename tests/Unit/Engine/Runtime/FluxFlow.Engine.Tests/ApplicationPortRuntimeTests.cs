using FluxFlow.Testing;
using FluxFlow.Composition.Definition;
using System.Threading.Tasks.Dataflow;
using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Core;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed partial class ApplicationPortRuntimeTests
{
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("Main", "Processor", "Input");
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("Main", "Source", "Output");
    private static readonly ApplicationAddress Signal =
        ApplicationAddress.WorkflowPort("Main", "Trigger", "Ack");

    [Fact]
    public async Task Stable_signal_input_accepts_multiple_payload_types_and_is_directly_addressable()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddSignalInput(Signal, capacity: 3)
            .Build();
        var target = new BufferBlock<FlowMessage>();
        await using var attachment = await runtime.AttachSignalInputAsync(Signal, target);

        var first = FlowMessage.Create("first");
        var second = FlowMessage.Create(42);
        (await runtime.SendAsync(Signal, first)).IsAccepted.ShouldBeTrue();
        (await runtime.SendAsync(Signal, second)).IsAccepted.ShouldBeTrue();
        (await runtime.SendAsync(Signal, FlowMessage.Create(true))).IsAccepted.ShouldBeTrue();

        var receivedFirst = await target.ReceiveAsync(TimeSpan.FromSeconds(5));
        var receivedSecond = await target.ReceiveAsync(TimeSpan.FromSeconds(5));
        var receivedThird = await target.ReceiveAsync(TimeSpan.FromSeconds(5));
        receivedFirst.Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("first");
        receivedSecond.Value.ShouldNotBeNull().Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(42);
        receivedThird.Value.ShouldNotBeNull().Json.Deserialize<bool>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBeTrue();
        receivedFirst.TraceId.ShouldBe(first.TraceId);
        receivedSecond.TraceId.ShouldBe(second.TraceId);

        var metadata = runtime.Ports.Single(port => port.Address == Signal);
        metadata.Kind.ShouldBe(ApplicationPortKind.Signal);
        metadata.PayloadType.ShouldBe(typeof(object));
        runtime.Status.Ports.Single(port => port.Address == Signal)
            .Kind.ShouldBe(ApplicationPortKind.Signal);
    }

    [Fact]
    public async Task Message_and_signal_attachments_retire_idempotently_and_allow_replacement()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input)
            .AddSignalInput(Signal)
            .Build();
        var messageTarget = new BufferBlock<FlowMessage>();
        var signalTarget = new BufferBlock<FlowMessage>();
        var messageAttachment = await runtime.AttachInputAsync(Input, messageTarget);
        var signalAttachment = await runtime.AttachSignalInputAsync(Signal, signalTarget);

        await messageAttachment.DisposeAsync();
        await messageAttachment.DisposeAsync();
        await signalAttachment.DisposeAsync();
        await signalAttachment.DisposeAsync();

        (await runtime.SendAsync(Input, Message("retired")))
            .Status.ShouldBe(PortSendStatus.Unavailable);
        (await runtime.SendAsync(Signal, FlowMessage.Create("retired")))
            .Status.ShouldBe(PortSendStatus.Unavailable);

        var replacementMessages = new BufferBlock<FlowMessage>();
        var replacementSignals = new BufferBlock<FlowMessage>();
        await using var replacementMessageAttachment =
            await runtime.AttachInputAsync(Input, replacementMessages);
        await using var replacementSignalAttachment =
            await runtime.AttachSignalInputAsync(Signal, replacementSignals);

        (await runtime.SendAsync(Input, Message("message"))).IsAccepted.ShouldBeTrue();
        (await runtime.SendAsync(Signal, FlowMessage.Create("signal"))).IsAccepted.ShouldBeTrue();
        (await replacementMessages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("message");
        (await replacementSignals.ReceiveAsync(TimeSpan.FromSeconds(5)))
            .Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("signal");
    }

    [Fact]
    public async Task Compiled_routes_deliver_canonical_outputs_to_signal_inputs()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output)
            .AddSignalInput(Signal)
            .Build();
        var target = new BufferBlock<FlowMessage>();
        await using var targetAttachment = await runtime.AttachSignalInputAsync(Signal, target);
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);

        var definition = ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": { "Type": "source", "Output": "Trigger.Ack" },
                  "Trigger": { "Type": "trigger" }
                }
              }
            }
            """);
        var registry = LinkCompilerFixture.Surfaces(
        [
            LinkCompilerFixture.Surface("source", outputs: [ComponentPorts.Metadata<FlowValue>("Output")]),
            LinkCompilerFixture.Surface("trigger", inputs: [ComponentPorts.SignalMetadata("Ack")])
        ]);
        var compilation = new LinkCompilerFixture(registry).Compile(definition);
        compilation.IsValid.ShouldBeTrue();
        using var route = runtime.Connect(compilation.Links.ShouldHaveSingleItem());

        source.Post(Message("ack")).ShouldBeTrue();

        (await target.ReceiveAsync(TimeSpan.FromSeconds(5)))
            .Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("ack");
    }

    [Fact]
    public async Task Try_send_reports_unavailable_full_and_completed_without_waiting()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input, capacity: 1)
            .Build();
        var first = Message("first");

        runtime.TrySend(Input, first).Status.ShouldBe(PortSendStatus.Unavailable);

        var target = new BufferBlock<FlowMessage>(new DataflowBlockOptions
        {
            BoundedCapacity = 1
        });
        target.Post(Message("occupied")).ShouldBeTrue();
        await using var attachment = await runtime.AttachInputAsync(Input, target);

        runtime.TrySend(Input, first).Status.ShouldBe(PortSendStatus.Accepted);
        runtime.TrySend(Input, Message("overflow")).Status.ShouldBe(PortSendStatus.Full);

        runtime.Complete();
        runtime.TrySend(Input, Message("late")).Status.ShouldBe(PortSendStatus.Completed);
        (await target.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("occupied");
        (await target.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("first");
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Input_revision_swap_finishes_claimed_work_and_moves_queued_work_to_new_target()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input, capacity: 3)
            .Build();
        var oldTarget = new PostponedTarget<FlowMessage>();
        var oldAttachment = await runtime.AttachInputAsync(Input, oldTarget);

        (await runtime.SendAsync(Input, Message("claimed"))).IsAccepted.ShouldBeTrue();
        await oldTarget.Offered.WaitAsync(TimeSpan.FromSeconds(5));
        (await runtime.SendAsync(Input, Message("queued"))).IsAccepted.ShouldBeTrue();

        var newTarget = new BufferBlock<FlowMessage>(new DataflowBlockOptions
        {
            BoundedCapacity = 4
        });
        var swap = runtime.AttachInputAsync(Input, newTarget).AsTask();
        swap.IsCompleted.ShouldBeFalse();

        oldTarget.AcceptPostponed();
        var newAttachment = await swap.WaitAsync(TimeSpan.FromSeconds(5));
        oldTarget.Accepted.Single().Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("claimed");
        (await newTarget.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("queued");

        await oldAttachment.DisposeAsync();
        (await runtime.SendAsync(Input, Message("after-swap"))).IsAccepted.ShouldBeTrue();
        (await newTarget.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("after-swap");
        await newAttachment.DisposeAsync();
    }

    [Fact]
    public async Task Rejected_revision_keeps_claimed_message_for_next_attachment()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input)
            .Build();
        var rejected = new RejectingTarget<FlowMessage>();
        await using var rejectedAttachment = await runtime.AttachInputAsync(Input, rejected);

        (await runtime.SendAsync(Input, Message("retry"))).IsAccepted.ShouldBeTrue();
        var failure = await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        failure.Reason.ShouldBe(ApplicationPortRejectionReason.TargetRejected);

        var replacement = new BufferBlock<FlowMessage>();
        await using var replacementAttachment = await runtime.AttachInputAsync(Input, replacement);
        (await replacement.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("retry");
    }

    [Fact]
    public async Task Completion_drops_retained_work_with_a_terminal_rejection_when_no_target_remains()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input)
            .Build();
        var rejected = new RejectingTarget<FlowMessage>();
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
            .AddInput(Input)
            .Build();
        var oldTarget = new PostponedTarget<FlowMessage>();
        await using var oldAttachment = await runtime.AttachInputAsync(Input, oldTarget);
        (await runtime.SendAsync(Input, Message("claimed"))).IsAccepted.ShouldBeTrue();
        await oldTarget.Offered.WaitAsync(TimeSpan.FromSeconds(5));

        var replacement = new BufferBlock<FlowMessage>();
        var swap = runtime.AttachInputAsync(Input, replacement).AsTask();
        swap.IsCompleted.ShouldBeFalse();

        await runtime.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(async () => await swap);
    }

    [Fact]
    public async Task Direct_receive_observes_output_without_stealing_workflow_delivery()
    {
        var sinkAddress = ApplicationAddress.WorkflowPort("Main", "Sink", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output)
            .AddInput(sinkAddress)
            .Build();
        var sink = new BufferBlock<FlowMessage>();
        await using var sinkAttachment = await runtime.AttachInputAsync(sinkAddress, sink);
        using var route = runtime.Connect(CompileLinks(
            "\"Sink.Input\"",
            sinkNames: ["Sink"]).Single());
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);

        var receive = runtime.ReceiveAsync<FlowValue>(Output, TimeSpan.FromSeconds(5));
        var message = Message("broadcast");
        source.Post(message).ShouldBeTrue();

        var received = (await receive).Message!;
        received.Value.ShouldBeSameAs(message.Value);
        received.MessageId.ShouldBe(message.MessageId);
        received.TraceId.ShouldBe(message.TraceId);
        (await sink.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
    }

    [Fact]
    public async Task Source_completion_does_not_complete_stable_output_or_observation()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output, discardUnmatched: true)
            .Build();
        var firstSource = new BufferBlock<FlowMessage>();
        using var firstAttachment = runtime.AttachOutput(Output, firstSource);
        var observed = await runtime.ObserveAsync<FlowValue>(Output, capacity: 4);
        observed.Status.ShouldBe(PortObserveStatus.Started);
        await using var observation = observed.Observation!;

        firstSource.Post(Message("first")).ShouldBeTrue();
        (await observation.Messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("first");
        firstSource.Complete();
        await firstSource.Completion;

        var secondSource = new BufferBlock<FlowMessage>();
        using var secondAttachment = runtime.AttachOutput(Output, secondSource);
        secondSource.Post(Message("second")).ShouldBeTrue();
        (await observation.Messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("second");
        observation.Completion.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Receive_and_observe_report_unavailable_timeout_and_completion_as_results()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output)
            .Build();

        (await runtime.ReceiveAsync<FlowValue>(Output)).Status.ShouldBe(PortReceiveStatus.Unavailable);
        (await runtime.ObserveAsync<FlowValue>(Output)).Status.ShouldBe(PortObserveStatus.Unavailable);

        var source = new BufferBlock<FlowMessage>();
        using var attachment = runtime.AttachOutput(Output, source);
        (await runtime.ReceiveAsync<FlowValue>(Output, TimeSpan.FromMilliseconds(30)))
            .Status.ShouldBe(PortReceiveStatus.TimedOut);

        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        (await runtime.ReceiveAsync<FlowValue>(Output)).Status.ShouldBe(PortReceiveStatus.Completed);
        (await runtime.ObserveAsync<FlowValue>(Output)).Status.ShouldBe(PortObserveStatus.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conditional_failure_isolated_from_healthy_sibling(bool fallback)
    {
        var failingAddress = ApplicationAddress.WorkflowPort("Main", "Failing", "Input");
        var healthyAddress = ApplicationAddress.WorkflowPort("Main", "Healthy", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output)
            .AddInput(failingAddress)
            .AddInput(healthyAddress)
            .Build();
        var failing = new BufferBlock<FlowMessage>();
        var healthy = new BufferBlock<FlowMessage>();
        await using var failingAttachment = await runtime.AttachInputAsync(failingAddress, failing);
        await using var healthyAttachment = await runtime.AttachInputAsync(healthyAddress, healthy);
        var healthyDeclaration = fallback ? "{\"Port\":\"Healthy.Input\",\"Fallback\":true}" : "\"Healthy.Input\"";
        var links = CompileLinks(
            "[{ \"Port\": \"Failing.Input\", \"Condition\": \"fail\" }, " + healthyDeclaration + "]",
            sinkNames: ["Failing", "Healthy"],
            expressionEngine: new FailingExpressionEngine());
        using var firstRoute = runtime.Connect(links[0].WithDelivery(FlowDeliveryPolicy.IsolateRejection));
        using var secondRoute = runtime.Connect(links[1]);
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);
        var diagnostics = new BufferBlock<FlowMessage>();
        var events = new BufferBlock<FlowMessage>();
        using var diagnosticLink = runtime.Diagnostics.LinkTo(diagnostics);
        using var eventLink = runtime.SystemEvents.LinkTo(events);
        var message = Message("value");

        source.Post(message).ShouldBeTrue();

        (await healthy.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(message);
        var rejection = await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        rejection.Reason.ShouldBe(ApplicationPortRejectionReason.ConditionFailed);
        rejection.CorrelationId.ShouldBe(message.CorrelationId);
        rejection.TraceId.ShouldBe(message.TraceId);
        rejection.MessageId.ShouldBe(message.MessageId);
        var diagnostic = await ReceiveUntilAsync(
            diagnostics,
            message => message.ToFlowEvent().Name == ApplicationDiagnosticNames.PortRejected);
        diagnostic.CorrelationId.ShouldBe(message.CorrelationId);
        diagnostic.TraceId.ShouldBe(message.TraceId);
        diagnostic.CausationId.ShouldBe(message.MessageId);
        diagnostic.ToFlowEvent().Name.ShouldBe(ApplicationDiagnosticNames.PortRejected);
        var systemEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        systemEvent.CorrelationId.ShouldBe(message.CorrelationId);
        systemEvent.TraceId.ShouldBe(message.TraceId);
        systemEvent.CausationId.ShouldBe(message.MessageId);
        systemEvent.ToFlowEvent().Name.ShouldBe(ApplicationSystemEventNames.LinkConditionFailed);
        failing.TryReceive(out _).ShouldBeFalse();

        source.Post(Message("next")).ShouldBeTrue();
        (await healthy.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("next");
    }

    [Fact]
    public async Task Full_target_does_not_block_healthy_fanout_sibling()
    {
        var slowAddress = ApplicationAddress.WorkflowPort("Main", "Slow", "Input");
        var healthyAddress = ApplicationAddress.WorkflowPort("Main", "Healthy", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output)
            .AddInput(slowAddress, capacity: 1)
            .AddInput(healthyAddress)
            .Build();
        var slow = new PostponedTarget<FlowMessage>();
        var healthy = new BufferBlock<FlowMessage>();
        await using var slowAttachment = await runtime.AttachInputAsync(slowAddress, slow);
        await using var healthyAttachment = await runtime.AttachInputAsync(healthyAddress, healthy);
        var links = CompileLinks(
            "[\"Slow.Input\", \"Healthy.Input\"]",
            sinkNames: ["Slow", "Healthy"]);
        using var firstRoute = runtime.Connect(links.Single(link => link.Target == slowAddress)
            .WithDelivery(FlowDeliveryPolicy.IsolateRejection));
        using var secondRoute = runtime.Connect(links.Single(link => link.Target == healthyAddress));
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);

        source.Post(Message("one")).ShouldBeTrue();
        await slow.Offered.WaitAsync(TimeSpan.FromSeconds(5));
        source.Post(Message("two")).ShouldBeTrue();

        (await healthy.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("one");
        (await healthy.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("two");
        var rejection = await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        rejection.Port.ShouldBe(slowAddress);
        rejection.Reason.ShouldBe(ApplicationPortRejectionReason.Full);
        slow.AcceptPostponed();
    }

    [Fact]
    public async Task Slow_observation_overflow_does_not_block_sibling_observation()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output, discardUnmatched: true)
            .Build();
        var source = new BufferBlock<FlowMessage>();
        using var sourceAttachment = runtime.AttachOutput(Output, source);
        var slowResult = await runtime.ObserveAsync<FlowValue>(Output, capacity: 1);
        var fastResult = await runtime.ObserveAsync<FlowValue>(Output, capacity: 4);
        await using var slow = slowResult.Observation!;
        await using var fast = fastResult.Observation!;

        source.Post(Message("one")).ShouldBeTrue();
        source.Post(Message("two")).ShouldBeTrue();

        (await fast.Messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("one");
        (await fast.Messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("two");
        (await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Reason.ShouldBe(ApplicationPortRejectionReason.ObservationOverflowed);
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await slow.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public Task Canonical_observation_overflow_faults_without_stopping_output_delivery()
        => AssertCanonicalOutputObservationOverflowAsync<FlowValue>();

    [Fact]
    public Task Converted_observation_overflow_faults_without_stopping_output_delivery()
        => AssertCanonicalOutputObservationOverflowAsync<string>();

    private static async Task AssertCanonicalOutputObservationOverflowAsync<T>()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output, discardUnmatched: true)
            .Build();
        var source = new BufferBlock<FlowMessage>();
        await using var builder = runtime.CreateRevision("observer-test").AttachOutput(Output, source);
        await using var revision = builder.Build();
        await using var lease = await revision.ActivateAsync();
        var result = await runtime.ObserveAsync<T>(Output, capacity: 1);
        result.Status.ShouldBe(PortObserveStatus.Started);
        await using var observation = result.Observation!;

        var firstReceive = runtime.ReceiveAsync<FlowValue>(Output);
        source.Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("first", new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeTrue();
        (await firstReceive.WaitAsync(TimeSpan.FromSeconds(5))).Message!.Value.Json
            .Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("first");
        // Prove the consumer buffer is full before sending the overflow message.
        (await observation.Messages.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();
        var overflow = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("overflow", new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: CorrelationId.New());
        var secondReceive = runtime.ReceiveAsync<FlowValue>(Output);
        source.Post(overflow).ShouldBeTrue();
        (await secondReceive.WaitAsync(TimeSpan.FromSeconds(5))).Message!.MessageId
            .ShouldBe(overflow.MessageId);

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await observation.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        failure.Message.ShouldBe($"Observation of output port '{Output}' exceeded its capacity of 1.");
        var rejection = await runtime.Rejections.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        rejection.Reason.ShouldBe(ApplicationPortRejectionReason.ObservationOverflowed);
        rejection.Port.ShouldBe(Output);
        rejection.MessageId.ShouldBe(overflow.MessageId);
        rejection.TraceId.ShouldBe(overflow.TraceId);
        rejection.CorrelationId.ShouldBe(overflow.CorrelationId);
        rejection.Exception.ShouldBeSameAs(failure);

        var afterReceive = runtime.ReceiveAsync<FlowValue>(Output);
        source.Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("after", new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeTrue();
        (await afterReceive.WaitAsync(TimeSpan.FromSeconds(5))).Message!.Value.Json
            .Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("after");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Canonical_observation_drains_and_completes_without_false_overflow(bool dispose)
        => AssertCanonicalOutputObservationCompletionAsync<FlowValue>(dispose);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Converted_observation_drains_and_completes_without_false_overflow(bool dispose)
        => AssertCanonicalOutputObservationCompletionAsync<string>(dispose);

    private static async Task AssertCanonicalOutputObservationCompletionAsync<T>(bool dispose)
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output, discardUnmatched: true)
            .Build();
        var source = new BufferBlock<FlowMessage>();
        await using var builder = runtime.CreateRevision("observer-lifecycle").AttachOutput(Output, source);
        await using var revision = builder.Build();
        await using var lease = await revision.ActivateAsync();
        var result = await runtime.ObserveAsync<T>(Output, capacity: 1);
        await using var observation = result.Observation!;
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("queued", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        source.Post(message).ShouldBeTrue();
        (await observation.Messages.OutputAvailableAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .ShouldBeTrue();

        if (dispose)
        {
            await observation.DisposeAsync();
            await observation.DisposeAsync();
        }
        else
        {
            runtime.Complete();
        }

        var received = await observation.Messages.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.MessageId.ShouldBe(message.MessageId);
        received.TraceId.ShouldBe(message.TraceId);
        var payload = received.Value is FlowValue value ? value.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) : (string)(object)received.Value!;
        payload.ShouldBe("queued");
        await observation.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        observation.Completion.IsCompletedSuccessfully.ShouldBeTrue();

        if (dispose)
        {
            var afterReceive = runtime.ReceiveAsync<FlowValue>(Output);
            source.Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("after-dispose", new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeTrue();
            (await afterReceive.WaitAsync(TimeSpan.FromSeconds(5))).Message!.Value.Json
                .Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("after-dispose");
        }

        runtime.Complete();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        ((IReceivableSourceBlock<ApplicationPortRejection>)runtime.Rejections)
            .TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Request_reply_registers_before_send_and_matches_exchange_correlation()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input)
            .AddOutput(Output)
            .Build();
        var responses = new BufferBlock<FlowMessage>();
        using var outputAttachment = runtime.AttachOutput(Output, responses);
        var processor = new ActionBlock<FlowMessage>(request =>
        {
            responses.Post(Message("wrong"));
            responses.Post(request.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("right", new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        });
        await using var inputAttachment = await runtime.AttachInputAsync(Input, processor);
        var request = FlowMessage.Create("request");

        var result = await runtime.SendAndReceiveAsync<string, string>(
            Input,
            Output,
            request,
            TimeSpan.FromSeconds(5));

        result.Status.ShouldBe(PortRequestStatus.Received);
        result.Response!.Value.ShouldBe("right");
        result.Response.TraceId.ShouldBe(request.TraceId);
        result.Response.CorrelationId.ShouldNotBeNull();
        result.Response.CorrelationId.ShouldNotBe(request.CorrelationId);
    }

    [Fact]
    public async Task Builder_and_runtime_reject_invalid_address_direction_and_port_kind()
    {
        Should.Throw<ArgumentException>(() => new ApplicationPortRuntimeBuilder()
            .AddInput(ApplicationAddress.SystemEvents));
        Should.Throw<ArgumentException>(() => new ApplicationPortRuntimeBuilder()
            .AddOutput(ApplicationAddress.Resource("Broker")));
        Should.Throw<InvalidOperationException>(() => new ApplicationPortRuntimeBuilder()
            .AddInput(Input)
            .AddOutput(Input));

        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input)
            .AddOutput(Output)
            .Build();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await runtime.AttachSignalInputAsync(Input, new BufferBlock<FlowMessage>()));
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await runtime.ReceiveAsync<FlowValue>(Input));
    }

    [Fact]
    public async Task Pre_canceled_direct_operations_remain_canceled()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input)
            .AddOutput(Output)
            .Build();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.SendAsync(Input, Message("value"), canceled.Token));
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.ReceiveAsync<FlowValue>(Output, cancellationToken: canceled.Token));
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.ObserveAsync<FlowValue>(Output, cancellationToken: canceled.Token));
    }

    [Fact]
    public async Task Prepared_revision_stages_outputs_and_swaps_routing_as_one_snapshot()
    {
        var firstInput = ApplicationAddress.WorkflowPort("Main", "First", "Input");
        var secondInput = ApplicationAddress.WorkflowPort("Main", "Second", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output)
            .AddInput(firstInput)
            .AddInput(secondInput)
            .Build();
        var firstTarget = new BufferBlock<FlowMessage>();
        var secondTarget = new BufferBlock<FlowMessage>();
        await using var firstAttachment = await runtime.AttachInputAsync(firstInput, firstTarget);
        await using var secondAttachment = await runtime.AttachInputAsync(secondInput, secondTarget);
        var firstSource = new BufferBlock<FlowMessage>();

        await using var firstBuilder = runtime.CreateRevision("revision-1")
            .AttachOutput(Output, firstSource)
            .SetLinks(CompileLinks("\"First.Input\"", ["First", "Second"]));
        await using var firstRevision = firstBuilder.Build();
        await using var firstLease = await firstRevision.ActivateAsync();

        firstSource.Post(Message("first-route")).ShouldBeTrue();
        (await firstTarget.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("first-route");

        var secondSource = new BufferBlock<FlowMessage>();
        await using var secondBuilder = runtime.CreateRevision("revision-2")
            .AttachOutput(Output, secondSource)
            .SetLinks(CompileLinks("\"Second.Input\"", ["First", "Second"]));
        await using var secondRevision = secondBuilder.Build();
        secondSource.Post(Message("staged")).ShouldBeTrue();
        secondTarget.TryReceive(out _).ShouldBeFalse();

        await using var secondLease = await secondRevision.ActivateAsync();
        firstSource.Post(Message("existing-source")).ShouldBeTrue();

        var received = new[]
        {
            (await secondTarget.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            (await secondTarget.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        received.OrderBy(static value => value, StringComparer.Ordinal)
            .ShouldBe(["existing-source", "staged"]);
        firstTarget.TryReceive(out _).ShouldBeFalse();
        runtime.CurrentRevision!.RevisionId.ShouldBe("revision-2");
        runtime.CurrentRevision.Sequence.ShouldBe(2);
        firstLease.Info.Sequence.ShouldBe(1);
        secondLease.Info.Sequence.ShouldBe(2);
    }

    [Fact]
    public async Task Canceled_revision_keeps_the_current_input_attachment_and_revision()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddInput(Input, capacity: 3)
            .Build();
        var oldTarget = new PostponedTarget<FlowMessage>();
        await using var oldAttachment = await runtime.AttachInputAsync(Input, oldTarget);
        (await runtime.SendAsync(Input, Message("claimed"))).IsAccepted.ShouldBeTrue();
        await oldTarget.Offered.WaitAsync(TimeSpan.FromSeconds(5));
        (await runtime.SendAsync(Input, Message("queued"))).IsAccepted.ShouldBeTrue();
        var replacement = new BufferBlock<FlowMessage>();
        await using var builder = runtime.CreateRevision("canceled")
            .ReplaceInput(Input, replacement);
        await using var revision = builder.Build();
        using var cancellation = new CancellationTokenSource();

        var activation = revision.ActivateAsync(cancellation.Token).AsTask();
        activation.IsCompleted.ShouldBeFalse();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await activation);
        runtime.CurrentRevision.ShouldBeNull();

        oldTarget.AcceptPostponed();
        await EventuallyAsync(() => Task.FromResult(oldTarget.HasPostponed));
        oldTarget.AcceptPostponed();
        oldTarget.Accepted.Select(static message => message.Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .ShouldBe(["claimed", "queued"]);
        replacement.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Faulted_prepared_source_rejects_activation_without_changing_current_routing()
    {
        var firstInput = ApplicationAddress.WorkflowPort("Main", "First", "Input");
        var secondInput = ApplicationAddress.WorkflowPort("Main", "Second", "Input");
        await using var runtime = new ApplicationPortRuntimeBuilder()
            .AddOutput(Output)
            .AddInput(firstInput)
            .AddInput(secondInput)
            .Build();
        var firstTarget = new BufferBlock<FlowMessage>();
        var secondTarget = new BufferBlock<FlowMessage>();
        await using var firstAttachment = await runtime.AttachInputAsync(firstInput, firstTarget);
        await using var secondAttachment = await runtime.AttachInputAsync(secondInput, secondTarget);
        var currentSource = new BufferBlock<FlowMessage>();
        await using var currentBuilder = runtime.CreateRevision("current")
            .AttachOutput(Output, currentSource)
            .SetLinks(CompileLinks("\"First.Input\"", ["First", "Second"]));
        await using var currentRevision = currentBuilder.Build();
        await using var currentLease = await currentRevision.ActivateAsync();

        var faultedSource = new BufferBlock<FlowMessage>();
        await using var rejectedBuilder = runtime.CreateRevision("rejected")
            .AttachOutput(Output, faultedSource)
            .SetLinks(CompileLinks("\"Second.Input\"", ["First", "Second"]));
        await using var rejectedRevision = rejectedBuilder.Build();
        ((IDataflowBlock)faultedSource).Fault(new InvalidOperationException("source failed"));
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await faultedSource.Completion);
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await rejectedRevision.ActivateAsync());

        runtime.CurrentRevision!.RevisionId.ShouldBe("current");
        currentSource.Post(Message("still-current")).ShouldBeTrue();
        (await firstTarget.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Value.ShouldNotBeNull().Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe("still-current");
        secondTarget.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Revision_event_sink_publishes_a_reliable_system_event()
    {
        await using var runtime = new ApplicationPortRuntimeBuilder().Build();
        var events = new BufferBlock<FlowMessage>();
        using var eventLink = runtime.SystemEvents.LinkTo(events);
        var timestamp = DateTimeOffset.UtcNow;
        var revisionEvent = new ApplicationRevisionEvent(
            7,
            "revision-7",
            timestamp,
            ApplicationRevisionPhase.Activated,
            [ApplicationAddress.Resource("broker")],
            ["Main"],
            new FluxFlow.Data.FlowError(
                "revision.warning",
                "Revision warning.",
                "Revision",
                false));

        (await runtime.PublishAsync(revisionEvent))
            .ShouldBeTrue();

        var message = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var @event = message.ToFlowEvent();
        @event.Timestamp.ShouldBe(timestamp);
        @event.Name.ShouldBe(ApplicationSystemEventNames.RevisionChanged);
        @event.Dimensions["category"].ShouldBe("Revision");
        @event.Dimensions["subject"].ShouldBe("revision-7");
        @event.Attributes["error"].ShouldNotBeNull();
        var details = @event.Details.Json;
        details.GetProperty("sequence").GetInt64().ShouldBe(7);
        details.GetProperty("phase").GetString().ShouldBe("Activated");
        details.GetProperty("resources").EnumerateArray().Single().GetString().ShouldBe("Resources.broker");
        details.GetProperty("workflows").EnumerateArray().Single().GetString().ShouldBe("Main");
    }

    private static FlowMessage Message(string payload)
        => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

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
        var registry = LinkCompilerFixture.Surfaces(
        [
            LinkCompilerFixture.Surface("source", outputs: [ComponentPorts.Metadata<FlowValue>("Output")]),
            LinkCompilerFixture.Surface("sink", inputs: [ComponentPorts.Metadata<FlowValue>("Input")])
        ]);
        var result = new LinkCompilerFixture(registry, expressionEngine).Compile(definition);
        result.IsValid.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        return result.Links;
    }


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

    private static async Task<FlowMessage> ReceiveUntilAsync(
        ISourceBlock<FlowMessage> source,
        Func<FlowMessage, bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            var message = await source.ReceiveAsync().WaitAsync(timeout - DateTime.UtcNow);
            if (predicate(message))
                return message;
        }

        throw new TimeoutException("Expected message was not received before the test timeout.");
    }

    private static async Task<FlowMessage<T>> ReceiveUntilAsync<T>(
        ISourceBlock<FlowMessage<T>> source,
        Func<FlowMessage<T>, bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            var message = await source.ReceiveAsync().WaitAsync(timeout - DateTime.UtcNow);
            if (predicate(message))
            {
                return message;
            }
        }

        throw new TimeoutException("Expected message was not received before the test timeout.");
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
        private readonly System.Threading.Channels.Channel<bool> _offers =
            System.Threading.Channels.Channel.CreateUnbounded<bool>();
        private readonly object _gate = new();
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _offered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ISourceBlock<T>? _source;
        private DataflowMessageHeader _header;

        public List<T> Accepted { get; } = [];

        public Task Offered => _offered.Task;

        public async Task NextOfferAsync()
            => await _offers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public bool HasPostponed
        {
            get
            {
                lock (_gate)
                    return _source is not null;
            }
        }

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
                _offers.Writer.TryWrite(true);
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
