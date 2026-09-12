using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class TriggerControlGraphTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trigger_return_control_link_drains_work_and_broker_outcome_before_application_stop(bool reject)
    {
        var acknowledged = Gate();
        var release = Gate();
        var session = new VNextRecordingMqttTransportSession
        {
            AcknowledgeHandler = async (_, _, token) =>
            {
                acknowledged.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        await using var controller = new MqttClientController(new MqttClientConfiguration
        {
            Name = "control-client", ClientId = "control-client",
            Broker = new MqttBrokerConfiguration { Host = "unused.invalid" },
            AutoConnect = MqttAutoConnectMode.OnStart,
            Reconnect = new MqttReconnectConfiguration { Enabled = false }
        }, new VNextRecordingMqttTransportFactory(() => session));
        await controller.StartAsync();
        var source = new MqttSubscriptionTriggerNode(controller, new MqttSubscriptionTriggerOptions
        {
            TriggerId = "receiver",
            Subscriptions = [MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
            {
                TopicFilter = "commands/+", Qos = MqttQos.AtLeastOnce
            })],
            WorkflowAcknowledgement = MqttWorkflowAcknowledgement.Required,
            BrokerAcknowledgement = MqttBrokerAcknowledgement.AfterOutcome,
            MaximumPendingMessages = 1,
            OutcomeTimeout = TimeSpan.FromMinutes(1)
        }, new FakeTimeProvider());
        var worker = new Worker();
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("main");
        var receiver = workflow.Add("receiver", source);
        var processing = workflow.Add("worker", worker);
        receiver.Output.LinkTo(processing.Input);
        var returnPort = receiver.Inputs[reject ? "Nak" : "Ack"];
        processing.Output.LinkTo(returnPort);
        app.GetInput(returnPort.Address).ShouldBeSameAs(returnPort);
        returnPort.IsControl.ShouldBeTrue();
        app.Validate();
        Task stopping;
        try
        {
            await app.StartAsync();
            using var timeout = new CancellationTokenSource(Deadline);
            while (session.Subscribed.IsEmpty) await Task.Delay(1, timeout.Token);
            await session.EmitAsync(new MqttReceivedApplicationMessage
            {
                Topic = "commands/run", Qos = MqttQos.AtLeastOnce,
                Timestamp = DateTimeOffset.UnixEpoch,
                Content = FlowContent.FromBytes("execute"u8.ToArray(), "text/plain")
            }, "control-delivery");
            await worker.Entered.Task.WaitAsync(Deadline);
            stopping = app.StopAsync();
            await source.ProductionCompletion.WaitAsync(Deadline);
            source.Ack.Completion.IsCompleted.ShouldBeFalse();
            source.Nak.Completion.IsCompleted.ShouldBeFalse();
            stopping.IsCompleted.ShouldBeFalse();
            session.Acknowledged.ShouldBeEmpty();
            worker.Release.TrySetResult();
            await acknowledged.Task.WaitAsync(Deadline);
            stopping.IsCompleted.ShouldBeFalse();
            source.Completion.IsCompleted.ShouldBeFalse();
            session.DisposeCount.ShouldBe(0);
        }
        finally { worker.Release.TrySetResult(); release.TrySetResult(); }

        await stopping.WaitAsync(Deadline);
        var outcome = session.Acknowledged.ShouldHaveSingleItem();
        outcome.Delivery.Value.ShouldBe("control-delivery");
        outcome.Outcome.ShouldBe(reject ? MqttWorkflowOutcome.Nak : MqttWorkflowOutcome.Ack);
        source.Ack.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        source.Nak.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        (await source.Ack.SendAsync(worker.Message!)).ShouldBeFalse();
        await app.DisposeAsync();
        session.Unsubscribed.ShouldBe(["filter:commands/+"]);
        session.DisposeCount.ShouldBe(0);
        session.IsConnected.ShouldBeTrue();
    }

    private sealed class Worker : FlowNode
    {
        public TaskCompletionSource Entered { get; } = Gate();
        public TaskCompletionSource Release { get; } = Gate();
        public FlowMessage? Message { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message)
        {
            Message = message;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(Stopping);
            await EmitAsync(message, Stopping);
        }
    }
}
