using System.Threading.Tasks.Dataflow;
using System.Text.Json;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttCommandRuntimeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData(MqttResultOrder.PreserveInput)]
    [InlineData(MqttResultOrder.Completion)]
    public async Task Command_results_retain_native_ordering_and_fanout(MqttResultOrder order)
    {
        var firstEntered = Gate();
        var release = Gate();
        var session = new VNextRecordingMqttTransportSession
        {
            PublishHandler = async (message, _) =>
            {
                if (message.Topic == "commands/1")
                {
                    firstEntered.TrySetResult();
                    await release.Task;
                }
            }
        };
        await using var controller = Controller(session);
        await controller.StartAsync();
        await using var node = new MqttControlNode(controller, new MqttControlOptions
        {
            RequestProcessing = MqttRequestProcessing.Concurrent,
            MaximumConcurrentRequests = 2,
            MaximumPendingRequests = 4,
            ResultOrder = order
        });
        var first = Sink(node.Output);
        var second = Sink(node.Output);
        var events = Sink(node.Events);
        var inputs = new[] { Request(1), Request(2) };
        var results = new List<FlowMessage>();
        try
        {
            (await node.Input.SendAsync(inputs[0])).ShouldBeTrue();
            await firstEntered.Task.WaitAsync(Timeout);
            (await node.Input.SendAsync(inputs[1])).ShouldBeTrue();
            var completed = await events.ReceiveAsync().WaitAsync(Timeout);
            completed.ToFlowEvent().Name.ShouldBe("mqtt.command.completed");
            completed.CorrelationId.ShouldBe(inputs[1].CorrelationId);
            if (order == MqttResultOrder.Completion)
            {
                results.Add(await first.ReceiveAsync().WaitAsync(Timeout));
                results[0].CausationId.ShouldBe(inputs[1].MessageId);
            }
            else first.Count.ShouldBe(0);
        }
        finally { release.TrySetResult(); }

        node.Complete();
        while (results.Count < 2) results.Add(await first.ReceiveAsync().WaitAsync(Timeout));
        var copies = new[]
        {
            await second.ReceiveAsync().WaitAsync(Timeout),
            await second.ReceiveAsync().WaitAsync(Timeout)
        };
        await node.Completion.WaitAsync(Timeout);
        await Task.WhenAll(first.Completion, second.Completion).WaitAsync(Timeout);
        var expected = order == MqttResultOrder.PreserveInput ? inputs : inputs.Reverse().ToArray();
        for (var index = 0; index < 2; index++)
        {
            results[index].IsError.ShouldBeFalse();
            results[index].CausationId.ShouldBe(expected[index].MessageId);
            results[index].CorrelationId.ShouldBe(expected[index].CorrelationId);
            copies[index].ShouldBeSameAs(results[index]);
        }
        session.Published.Select(message => message.Topic).ShouldBe(["commands/2", "commands/1"]);
        session.DisposeCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_fault_cancels_and_joins_active_controller_operation(bool faultOutput)
    {
        var entered = Gate();
        var cancelled = Gate();
        var release = Gate();
        var exited = Gate();
        var session = new VNextRecordingMqttTransportSession
        {
            PublishHandler = async (_, token) =>
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                entered.TrySetResult();
                try { await release.Task; }
                finally { exited.TrySetResult(); }
            }
        };
        await using var controller = Controller(session);
        await controller.StartAsync();
        await using var node = new MqttControlNode(controller);
        var failure = new InvalidOperationException("command stopped");
        Task? disposal = null;
        try
        {
            (await node.Input.SendAsync(Request(1))).ShouldBeTrue();
            await entered.Task.WaitAsync(Timeout);
            if (faultOutput) node.Output.Fault(failure);
            else node.Fault(failure);
            // The public completion may not publish while the operation is still held.
            node.Completion.IsCompleted.ShouldBeFalse();
            await cancelled.Task.WaitAsync(Timeout);
            disposal = node.DisposeAsync().AsTask();
            disposal.IsCompleted.ShouldBeFalse();
            exited.Task.IsCompleted.ShouldBeFalse();
            session.DisposeCount.ShouldBe(0);
        }
        finally { release.TrySetResult(); }

        var actual = await Should.ThrowAsync<InvalidOperationException>(
            () => node.Completion.WaitAsync(Timeout));
        actual.ShouldBeSameAs(failure);
        await disposal!.WaitAsync(Timeout);
        exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
        (await node.Input.SendAsync(Request(2))).ShouldBeFalse();
        await node.Events.Completion.WaitAsync(Timeout);
        session.DisposeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Command_runs_as_a_direct_component_without_owning_controller()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = Controller(session);
        await controller.StartAsync();
        var node = new MqttControlNode(controller);
        await using (node)
        {
            var component = ((object)node).ShouldBeAssignableTo<IFlowComponent>()!;
            component.Ports.Inputs["Input"].ShouldBeSameAs(node.Input);
            component.Ports.Outputs["Output"].ShouldBeSameAs(node.Output);
            component.Ports.Events.ShouldBeSameAs(node.Events);
            await using var application = new FlowApplication();
            var workflow = application.AddWorkflow("commands");
            var command = workflow.Add("publish", component);
            var capture = new CaptureNode();
            command.Output.LinkTo(workflow.Add("result", capture).Input);
            await application.StartAsync();
            var input = Request(4);
            (await application.SendAsync(command.Input, input)).ShouldBeTrue();
            var result = await capture.Received.Task.WaitAsync(Timeout);
            result.CausationId.ShouldBe(input.MessageId);
            result.Value!.Json.GetProperty("topic").GetString().ShouldBe("commands/4");
            await application.StopAsync().WaitAsync(Timeout);
        }
        session.DisposeCount.ShouldBe(0);
        controller.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task Command_expected_failure_is_error_data_and_does_not_stop_following_requests()
    {
        var expected = new FlowError("broker.busy", "retry later", "transport", true);
        var session = new VNextRecordingMqttTransportSession
        {
            PublishHandler = (message, _) => message.Topic == "commands/1"
                ? ValueTask.FromException(new MqttClientOperationException(MqttClientOperation.Publish, expected))
                : ValueTask.CompletedTask
        };
        await using var controller = Controller(session);
        await controller.StartAsync();
        await using var node = new MqttControlNode(controller);
        var output = Sink(node.Output);
        var events = Sink(node.Events);
        var first = Request(1);
        (await node.Input.SendAsync(first)).ShouldBeTrue();
        var failed = await output.ReceiveAsync().WaitAsync(Timeout);
        failed.Error.ShouldBeSameAs(expected);
        failed.CausationId.ShouldBe(first.MessageId);
        failed.CorrelationId.ShouldBe(first.CorrelationId);
        (await events.ReceiveAsync().WaitAsync(Timeout)).ToFlowEvent().Name.ShouldBe("mqtt.command.failed");

        var second = Request(2);
        (await node.Input.SendAsync(second)).ShouldBeTrue();
        node.Complete();
        var succeeded = await output.ReceiveAsync().WaitAsync(Timeout);
        succeeded.IsError.ShouldBeFalse();
        succeeded.CausationId.ShouldBe(second.MessageId);
        succeeded.Value!.Json.GetProperty("topic").GetString().ShouldBe("commands/2");
        await node.Completion.WaitAsync(Timeout);
        session.Published.Select(message => message.Topic).ShouldBe(["commands/2"]);
    }

    [Fact]
    public async Task Command_forwards_error_envelope_without_executing_controller()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = Controller(session);
        await controller.StartAsync();
        await using var node = new MqttControlNode(controller);
        var output = Sink(node.Output);
        var error = FlowMessage.CreateError(new FlowError("input.invalid", "invalid", "validation"));
        (await node.Input.SendAsync(error)).ShouldBeTrue();
        node.Complete();
        (await output.ReceiveAsync().WaitAsync(Timeout)).ShouldBeSameAs(error);
        await node.Completion.WaitAsync(Timeout);
        session.Published.ShouldBeEmpty();
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class CaptureNode : FlowNode
    {
        public TaskCompletionSource<FlowMessage> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task ProcessAsync(FlowMessage message)
        {
            Received.TrySetResult(message);
            return Task.CompletedTask;
        }
    }

    private static BufferBlock<FlowMessage> Sink(ISourceBlock<FlowMessage> source)
    {
        var sink = new BufferBlock<FlowMessage>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }

    private static MqttClientController Controller(VNextRecordingMqttTransportSession session) => new(
        new MqttClientConfiguration
        {
            Name = "command-test",
            ClientId = "command-test",
            Broker = new MqttBrokerConfiguration { Host = "in-process.invalid" }
        }, new VNextRecordingMqttTransportFactory(() => session));

    private static FlowMessage Request(int number) => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement<MqttClientRequest>(
            new MqttPublishClientRequest
            {
                Message = new MqttPublishMessage
                {
                    Topic = $"commands/{number}",
                    Content = FlowContent.FromBytes(new byte[] { 1 })
                }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
}
