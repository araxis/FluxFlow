using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class TriggerOutcomeCleanupTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, true)]
    public async Task Trigger_disposal_joins_outcomes_and_releases_registration(
        bool outcomeFails, bool registrationFails, bool timerFails, bool stopFails = false)
    {
        await using var scenario = new Scenario(outcomeFails, registrationFails, timerFails);
        await scenario.BeginTimeoutAsync();
        scenario.Trigger.Complete();
        await scenario.Trigger.Completion.WaitAsync(Deadline);
        scenario.Clock.StopFails = stopFails;

        var disposal = scenario.Trigger.DisposeAsync().AsTask();
        try
        {
            disposal.IsCompleted.ShouldBeFalse();
            scenario.Borrowed.RegistrationDisposeCalls.ShouldBe(0);
            scenario.Clock.DisposeCalls.ShouldBe(0);
        }
        finally { scenario.ReleaseOutcome.TrySetResult(); }

        var expected = new[]
        {
            stopFails ? scenario.Clock.StopFailure : null,
            outcomeFails ? scenario.OutcomeFailure : null,
            timerFails ? scenario.Clock.Failure : null,
            registrationFails ? scenario.Borrowed.RegistrationFailure : null
        }.OfType<Exception>().ToArray();
        if (expected.Length == 0)
            await disposal.WaitAsync(Deadline);
        else if (expected.Length == 1)
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() => disposal.WaitAsync(Deadline));
            error.ShouldBeSameAs(expected[0]);
        }
        else
        {
            var error = await Should.ThrowAsync<AggregateException>(() => disposal.WaitAsync(Deadline));
            var failures = error.Flatten().InnerExceptions;
            failures.Count.ShouldBe(expected.Length);
            for (var index = 0; index < expected.Length; index++)
                failures[index].ShouldBeSameAs(expected[index]);
        }

        await scenario.Trigger.DisposeAsync();
        scenario.Borrowed.RegistrationDisposeCalls.ShouldBe(1);
        scenario.Clock.DisposeCalls.ShouldBe(1);
        scenario.Borrowed.DisposeCalls.ShouldBe(0);
        scenario.Session.DisposeCount.ShouldBe(0);
        scenario.Session.Unsubscribed.ShouldBe(new[] { "filter:commands/+" });
        scenario.Session.Acknowledged.Count.ShouldBe(1);
        scenario.Session.Acknowledged.Single().Delivery.ShouldBe(new MqttTransportDeliveryToken("cleanup-delivery"));
        scenario.Session.Acknowledged.Single().Outcome.ShouldBe(MqttWorkflowOutcome.Timeout);
    }

    [Fact]
    public async Task Timeout_failure_faults_running_source_before_disposal()
    {
        await using var scenario = new Scenario(outcomeFails: true);
        await scenario.BeginTimeoutAsync();
        scenario.ReleaseOutcome.TrySetResult();
        var failure = await Should.ThrowAsync<InvalidOperationException>(
            () => scenario.Trigger.Completion.WaitAsync(Deadline));
        failure.ShouldBeSameAs(scenario.OutcomeFailure);
        try { await scenario.Trigger.DisposeAsync(); }
        catch (InvalidOperationException cleanupFailure)
        {
            // Disposal may also join the already-reported observer before its task exits.
            cleanupFailure.ShouldBeSameAs(scenario.OutcomeFailure);
        }
        scenario.Borrowed.RegistrationDisposeCalls.ShouldBe(1);
        scenario.Clock.DisposeCalls.ShouldBe(1);
        scenario.Borrowed.DisposeCalls.ShouldBe(0);
        scenario.Session.DisposeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Trigger_disposal_preserves_all_concurrent_outcome_failures()
    {
        await using var scenario = new Scenario(outcomeFails: true, deliveries: 2);
        await scenario.BeginTimeoutAsync();
        scenario.Trigger.Complete();
        await scenario.Trigger.Completion.WaitAsync(Deadline);
        var disposal = scenario.Trigger.DisposeAsync().AsTask();
        try { disposal.IsCompleted.ShouldBeFalse(); }
        finally { scenario.ReleaseOutcome.TrySetResult(); }
        var error = await Should.ThrowAsync<AggregateException>(() => disposal.WaitAsync(Deadline));
        var failures = error.Flatten().InnerExceptions;
        failures.Count.ShouldBe(2);
        failures.ShouldContain(value => ReferenceEquals(value, scenario.OutcomeFailure));
        failures.ShouldContain(value => ReferenceEquals(value, scenario.SecondOutcomeFailure));
        scenario.Borrowed.RegistrationDisposeCalls.ShouldBe(1);
        scenario.Clock.DisposeCalls.ShouldBe(1);
        scenario.Session.Acknowledged.Count.ShouldBe(2);
        scenario.Borrowed.DisposeCalls.ShouldBe(0);
        scenario.Session.DisposeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Trigger_disposal_settles_pending_outcomes_without_waiting_for_deadline()
    {
        await using var scenario = new Scenario(outcomeFails: false);
        await scenario.BeginDeliveryAsync();
        scenario.Trigger.Complete();
        await scenario.Trigger.Completion.WaitAsync(Deadline);
        await scenario.Trigger.DisposeAsync().AsTask().WaitAsync(Deadline);
        scenario.Clock.GetUtcNow().ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        scenario.Clock.DisposeCalls.ShouldBe(1);
        scenario.Borrowed.RegistrationDisposeCalls.ShouldBe(1);
        scenario.Session.Unsubscribed.ShouldBe(new[] { "filter:commands/+" });
        scenario.Session.Acknowledged.ShouldBeEmpty();
        scenario.OutcomeEntered.Task.IsCompleted.ShouldBeFalse();
        scenario.Borrowed.DisposeCalls.ShouldBe(0);
        scenario.Session.DisposeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Trigger_stop_failure_still_cleans_pending_deliveries()
    {
        await using var scenario = new Scenario(outcomeFails: false, deliveries: 2);
        await scenario.BeginDeliveryAsync();
        scenario.Trigger.Complete();
        await scenario.Trigger.Completion.WaitAsync(Deadline);
        scenario.Clock.StopFails = true;

        var error = await Should.ThrowAsync<InvalidOperationException>(
            () => scenario.Trigger.DisposeAsync().AsTask().WaitAsync(Deadline));
        error.ShouldBeSameAs(scenario.Clock.StopFailure);
        await scenario.Trigger.DisposeAsync();
        scenario.Clock.DisposeCalls.ShouldBe(1);
        scenario.Borrowed.RegistrationDisposeCalls.ShouldBe(1);
        scenario.Session.Unsubscribed.ShouldBe(new[] { "filter:commands/+" });
        scenario.Session.Acknowledged.ShouldBeEmpty();
        scenario.OutcomeEntered.Task.IsCompleted.ShouldBeFalse();
        scenario.Borrowed.DisposeCalls.ShouldBe(0);
        scenario.Session.DisposeCount.ShouldBe(0);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly MqttClientController _controller;
        private readonly BufferBlock<FlowMessage> _output = new(new DataflowBlockOptions { BoundedCapacity = 2 });
        private readonly int _deliveries;
        private int _entered;
        public Scenario(bool outcomeFails, bool registrationFails = false, bool timerFails = false, int deliveries = 1)
        {
            _deliveries = deliveries;
            Clock = new CleanupClock(timerFails);
            Session = new VNextRecordingMqttTransportSession
            {
                AcknowledgeHandler = async (delivery, _, _) =>
                {
                    if (Interlocked.Increment(ref _entered) == _deliveries)
                        OutcomeEntered.TrySetResult();
                    await ReleaseOutcome.Task;
                    if (outcomeFails)
                        throw delivery.Value == "cleanup-delivery-2" ? SecondOutcomeFailure : OutcomeFailure;
                }
            };
            _controller = new MqttClientController(new MqttClientConfiguration
            {
                Name = "cleanup-client", ClientId = "cleanup-client",
                Broker = new MqttBrokerConfiguration { Host = "unused.invalid" },
                AutoConnect = MqttAutoConnectMode.OnStart,
                Reconnect = new MqttReconnectConfiguration { Enabled = false }
            }, new VNextRecordingMqttTransportFactory(() => Session));
            Borrowed = new BorrowedController(_controller, registrationFails);
            Trigger = new MqttSubscriptionTriggerNode(Borrowed, new MqttSubscriptionTriggerOptions
            {
                TriggerId = "cleanup-trigger",
                Subscriptions = [MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                {
                    TopicFilter = "commands/+", Qos = MqttQos.AtLeastOnce
                })],
                WorkflowAcknowledgement = MqttWorkflowAcknowledgement.Required,
                BrokerAcknowledgement = MqttBrokerAcknowledgement.AfterOutcome,
                MaximumPendingMessages = 4,
                OutcomeTimeout = TimeSpan.FromSeconds(1)
            }, Clock);
            Trigger.Output.LinkTo(_output, new DataflowLinkOptions { PropagateCompletion = true });
        }
        public InvalidOperationException OutcomeFailure { get; } = new("outcome failed");
        public InvalidOperationException SecondOutcomeFailure { get; } = new("second outcome failed");
        public TaskCompletionSource OutcomeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOutcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public VNextRecordingMqttTransportSession Session { get; }
        public BorrowedController Borrowed { get; }
        public CleanupClock Clock { get; }
        public MqttSubscriptionTriggerNode Trigger { get; }
        public async Task BeginTimeoutAsync()
        {
            await BeginDeliveryAsync();
            Clock.Inner.Advance(TimeSpan.FromSeconds(1));
            await OutcomeEntered.Task.WaitAsync(Deadline);
        }
        public async Task BeginDeliveryAsync()
        {
            await _controller.StartAsync();
            await Trigger.StartAsync();
            await Borrowed.Registered.Task.WaitAsync(Deadline);
            for (var index = 0; index < _deliveries; index++)
            {
                await Session.EmitAsync(new MqttReceivedApplicationMessage
                {
                    Topic = "commands/run", Qos = MqttQos.AtLeastOnce,
                    Timestamp = Clock.GetUtcNow(), Content = FlowContent.FromBytes("execute"u8.ToArray(), "text/plain")
                }, index == 0 ? "cleanup-delivery" : "cleanup-delivery-2");
                var message = await _output.ReceiveAsync().WaitAsync(Deadline);
                message.Value!.Deserialize<MqttReceivedApplicationMessage>()!.Topic.ShouldBe("commands/run");
            }
        }
        public async ValueTask DisposeAsync()
        {
            ReleaseOutcome.TrySetResult();
            try { await Trigger.DisposeAsync().AsTask().WaitAsync(Deadline); }
            catch { /* The tests assert expected cleanup failures before fixture teardown. */ }
            await _controller.DisposeAsync();
        }
    }

    private sealed class BorrowedController(IMqttClientController inner, bool registrationFails) : IMqttClientController
    {
        private readonly bool _registrationFails = registrationFails;
        public string Name => inner.Name;
        public bool IsConnected => inner.IsConnected;
        public MqttTransportCapabilities Capabilities => inner.Capabilities;
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public InvalidOperationException RegistrationFailure { get; } = new("registration cleanup failed");
        public int RegistrationDisposeCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);
        public ValueTask<MqttClientResult> ExecuteAsync(MqttClientRequest request, CancellationToken cancellationToken = default)
            => inner.ExecuteAsync(request, cancellationToken);
        public ValueTask<IMqttClientEventSubscription> SubscribeEventsAsync(int capacity = 128, CancellationToken cancellationToken = default)
            => inner.SubscribeEventsAsync(capacity, cancellationToken);
        public async ValueTask<IMqttTriggerRegistration> RegisterTriggerAsync(MqttTriggerRegistrationOptions options, CancellationToken cancellationToken = default)
        {
            var registration = await inner.RegisterTriggerAsync(options, cancellationToken);
            Registered.TrySetResult();
            return new Registration(registration, this);
        }
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
        private sealed class Registration(IMqttTriggerRegistration inner, BorrowedController owner) : IMqttTriggerRegistration
        {
            public IAsyncEnumerable<MqttTriggerDelivery> Messages => inner.Messages;
            public async ValueTask DisposeAsync()
            {
                owner.RegistrationDisposeCalls++;
                await inner.DisposeAsync();
                if (owner._registrationFails) throw owner.RegistrationFailure;
            }
        }
    }

    private sealed class CleanupClock(bool fails) : TimeProvider
    {
        private readonly bool _fails = fails;
        public FakeTimeProvider Inner { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        public InvalidOperationException Failure { get; } = new("timer cleanup failed");
        public InvalidOperationException StopFailure { get; } = new("timer stop failed");
        public bool StopFails { get; set; }
        public int DisposeCalls { get; private set; }
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new Timer(Inner.CreateTimer(callback, state, dueTime, period), this);
        private sealed class Timer(ITimer inner, CleanupClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (owner.StopFails && dueTime == Timeout.InfiniteTimeSpan)
                    throw owner.StopFailure;
                return inner.Change(dueTime, period);
            }
            public void Dispose() => throw new NotSupportedException("Cleanup must await timer disposal.");
            public async ValueTask DisposeAsync()
            {
                owner.DisposeCalls++;
                await inner.DisposeAsync();
                if (owner._fails) throw owner.Failure;
            }
        }
    }
}
