using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Core;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class TransportSourceLifetimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transport_sources_are_direct_components_and_subscribe_only_on_start(bool trigger)
    {
        var controller = new Controller();
        var source = Source(controller, trigger);
        await using var app = new FlowApplication();
        var component = app.AddWorkflow("main").Add("source", source);
        component.Output.DiscardUnmatched();
        component.Instance.ShouldBeSameAs(source);
        source.Ports.Outputs["Output"].ShouldBeSameAs(source.Output);
        source.Ports.Events.ShouldBeSameAs(source.Events);
        controller.SubscriptionCalls.ShouldBe(0);

        await app.StartAsync();
        await source.StartAsync();
        await controller.Subscription.Entered.Task.WaitAsync(Deadline);
        controller.SubscriptionCalls.ShouldBe(1);
        controller.RequestedCapacity.ShouldBe(2);
        controller.StartCalls.ShouldBe(0);
        if (trigger)
        {
            controller.Registration!.TriggerId.ShouldBe("receiver");
            controller.Registration.Subscriptions.Single().Name.ShouldBe("commands");
            var receiver = (MqttSubscriptionTriggerNode)source;
            source.Ports.Controls["Ack"].ShouldBeSameAs(receiver.Ack);
            source.Ports.Controls["Nak"].ShouldBeSameAs(receiver.Nak);
            component.Inputs["Ack"].IsControl.ShouldBeTrue();
            component.Inputs["Nak"].IsControl.ShouldBeTrue();
        }

        await app.StopAsync().WaitAsync(Deadline);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        source.Ports.Controls.Values.All(input => input.Completion.IsCompletedSuccessfully).ShouldBeTrue();
        controller.Subscription.Exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
        controller.Subscription.DisposeCalls.ShouldBe(0);
        await app.DisposeAsync();
        controller.Subscription.DisposeCalls.ShouldBe(1);
        controller.DisposeCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_fault_joins_active_enumeration_before_disposing_subscription(bool trigger)
    {
        var controller = new Controller();
        controller.Subscription.HoldOnCancellation = true;
        await using var source = Source(controller, trigger);
        await source.StartAsync();
        await controller.Subscription.Entered.Task.WaitAsync(Deadline);
        var failure = new InvalidOperationException("held source failed");
        Task disposing;
        try
        {
            source.Fault(failure);
            await controller.Subscription.Cancelled.Task.WaitAsync(Deadline);
            disposing = source.DisposeAsync().AsTask();
            source.Completion.IsCompleted.ShouldBeFalse();
            disposing.IsCompleted.ShouldBeFalse();
            controller.Subscription.DisposeCalls.ShouldBe(0);
            controller.Subscription.Exited.Task.IsCompleted.ShouldBeFalse();
        }
        finally { controller.Subscription.Release.TrySetResult(); }

        var observed = await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline));
        observed.ShouldBeSameAs(failure);
        await disposing.WaitAsync(Deadline);
        controller.Subscription.Exited.Task.IsCompletedSuccessfully.ShouldBeTrue();
        controller.Subscription.DisposedWhileActive.ShouldBeFalse();
        controller.Subscription.DisposeCalls.ShouldBe(1);
        controller.DisposeCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_stop_before_start_does_not_subscribe(bool trigger)
    {
        var controller = new Controller();
        await using var source = Source(controller, trigger);
        source.Complete();
        await source.Completion.WaitAsync(Deadline);
        await source.StartAsync();
        await source.DisposeAsync();
        controller.SubscriptionCalls.ShouldBe(0);
        controller.Subscription.DisposeCalls.ShouldBe(0);
        controller.DisposeCalls.ShouldBe(0);
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_source_start_can_be_retried_without_losing_subscription(bool trigger)
    {
        var controller = new Controller();
        await using var source = Source(controller, trigger);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => source.StartAsync(cancellation.Token));
        controller.SubscriptionCalls.ShouldBe(0);
        source.Completion.IsCompleted.ShouldBeFalse();

        await source.StartAsync();
        await controller.Subscription.Entered.Task.WaitAsync(Deadline);
        source.Complete();
        await source.Completion.WaitAsync(Deadline);
        await source.DisposeAsync();
        controller.SubscriptionCalls.ShouldBe(1);
        controller.Subscription.DisposeCalls.ShouldBe(1);
        controller.DisposeCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Event_source_fans_out_original_envelopes_on_the_Core_graph()
    {
        var controller = new Controller();
        var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        controller.Subscription.Items = Enumerable.Range(0, 3)
            .Select(index => new MqttClientConnectedEvent($"client-{index}", timestamp.AddSeconds(index), false)).ToArray();
        controller.Subscription.Release.TrySetResult();
        var source = new MqttClientEventsNode(controller, 2);
        var first = new Sink();
        var second = new Sink();
        await using var app = new FlowApplication(linkCapacity: 2);
        var main = app.AddWorkflow("main");
        var output = main.Add("events", source).Output;
        output.LinkTo(main.Add("first", first).Input);
        output.LinkTo(app.AddWorkflow("reports").Add("second", second).Input);
        await app.StartAsync();
        await app.Completion.WaitAsync(Deadline);

        first.Messages.Count.ShouldBe(3);
        second.Messages.Count.ShouldBe(3);
        for (var index = 0; index < 3; index++)
        {
            var message = first.Messages.ElementAt(index);
            message.ShouldBeSameAs(second.Messages.ElementAt(index));
            var value = message.Value!.Deserialize<MqttClientConnectedEvent>()!;
            value.Client.ShouldBe($"client-{index}");
            value.Timestamp.ShouldBe(timestamp.AddSeconds(index));
            value.Automatic.ShouldBeFalse();
        }
        first.Messages.Select(message => message.MessageId).Distinct().Count().ShouldBe(3);
        await app.DisposeAsync();
        controller.Subscription.DisposeCalls.ShouldBe(1);
        controller.DisposeCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Subscription_start_failure_faults_source_without_disposing_controller(bool trigger)
    {
        var failure = new InvalidOperationException("subscription unavailable");
        var controller = new Controller { SubscriptionFailure = failure };
        await using var source = Source(controller, trigger);
        await source.StartAsync();
        var observed = await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline));
        observed.ShouldBeSameAs(failure);
        await source.DisposeAsync();
        controller.SubscriptionCalls.ShouldBe(1);
        controller.Subscription.DisposeCalls.ShouldBe(0);
        controller.DisposeCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Subscription_disposal_failure_is_observable_and_not_retried(bool trigger)
    {
        var failure = new InvalidOperationException("subscription cleanup failed");
        var controller = new Controller();
        controller.Subscription.DisposeFailure = failure;
        controller.Subscription.Release.TrySetResult();
        var source = Source(controller, trigger);
        await source.StartAsync();
        await source.ProductionCompletion.WaitAsync(Deadline);
        source.Complete();
        await source.Completion.WaitAsync(Deadline);
        var observed = await Should.ThrowAsync<InvalidOperationException>(() => source.DisposeAsync().AsTask());
        observed.ShouldBeSameAs(failure);
        await source.DisposeAsync();
        controller.Subscription.DisposedWhileActive.ShouldBeFalse();
        controller.Subscription.DisposeCalls.ShouldBe(1);
        controller.DisposeCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    public void Source_options_reject_invalid_capacity_before_subscribing(bool trigger, int capacity)
    {
        var controller = new Controller();
        var error = Should.Throw<ArgumentOutOfRangeException>(() => Source(controller, trigger, capacity));
        error.ParamName.ShouldBe(trigger ? "MaximumPendingMessages" : "maximumPendingEvents");
        controller.SubscriptionCalls.ShouldBe(0);
        controller.StartCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_requires_an_explicit_controller(bool trigger)
        => Should.Throw<ArgumentNullException>(() => Source(null!, trigger)).ParamName.ShouldBe("controller");

    [Fact]
    public void Trigger_requires_options_without_starting_the_controller()
    {
        var controller = new Controller();
        Should.Throw<ArgumentNullException>(() => new MqttSubscriptionTriggerNode(controller, null!))
            .ParamName.ShouldBe("options");
        controller.SubscriptionCalls.ShouldBe(0);
        controller.StartCalls.ShouldBe(0);
    }

    private static FlowSource Source(Controller controller, bool trigger, int capacity = 2)
        => trigger ? new MqttSubscriptionTriggerNode(controller, new MqttSubscriptionTriggerOptions
        {
            TriggerId = "receiver", Subscriptions = [MqttSubscriptionTarget.Named("commands")], MaximumPendingMessages = capacity
        }) : new MqttClientEventsNode(controller, capacity);

    private sealed class Controller : IMqttClientController
    {
        public string Name => "controller";
        public bool IsConnected => true;
        public MqttTransportCapabilities Capabilities => new();
        public Subscription Subscription { get; } = new();
        public Exception? SubscriptionFailure { get; init; }
        public int SubscriptionCalls { get; private set; }
        public int RequestedCapacity { get; private set; }
        public int StartCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public MqttTriggerRegistrationOptions? Registration { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default) { StartCalls++; return Task.CompletedTask; }
        public ValueTask<MqttClientResult> ExecuteAsync(MqttClientRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Source fixtures must not execute commands.");
        public ValueTask<IMqttClientEventSubscription> SubscribeEventsAsync(int capacity = 128, CancellationToken cancellationToken = default)
        {
            SubscriptionCalls++;
            RequestedCapacity = capacity;
            return SubscriptionFailure is null ? ValueTask.FromResult<IMqttClientEventSubscription>(Subscription)
                : ValueTask.FromException<IMqttClientEventSubscription>(SubscriptionFailure);
        }
        public ValueTask<IMqttTriggerRegistration> RegisterTriggerAsync(MqttTriggerRegistrationOptions options, CancellationToken cancellationToken = default)
        {
            SubscriptionCalls++;
            RequestedCapacity = options.MaximumPendingMessages;
            Registration = options;
            return SubscriptionFailure is null ? ValueTask.FromResult<IMqttTriggerRegistration>(Subscription)
                : ValueTask.FromException<IMqttTriggerRegistration>(SubscriptionFailure);
        }
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class Subscription : IMqttClientEventSubscription, IMqttTriggerRegistration
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldOnCancellation { get; set; }
        public IReadOnlyList<MqttClientEvent> Items { get; set; } = [];
        public Exception? DisposeFailure { get; set; }
        public int DisposeCalls { get; private set; }
        public bool DisposedWhileActive { get; private set; }
        public IAsyncEnumerable<MqttClientEvent> Events => Read(Items);
        public IAsyncEnumerable<MqttTriggerDelivery> Messages => Read<MqttTriggerDelivery>([]);
        private async IAsyncEnumerable<T> Read<T>(IReadOnlyList<T> items, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Entered.TrySetResult();
            try
            {
                foreach (var item in items) yield return item;
                if (HoldOnCancellation) await Release.Task;
                else await Release.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally { Exited.TrySetResult(); }
        }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            DisposedWhileActive = Entered.Task.IsCompleted && !Exited.Task.IsCompleted;
            return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
        }
    }

    private sealed class Sink : IFlowComponent
    {
        private readonly ActionBlock<FlowMessage> _input;
        public Sink()
        {
            _input = new ActionBlock<FlowMessage>(Messages.Enqueue, new ExecutionDataflowBlockOptions { BoundedCapacity = 2 });
            Ports = new FlowPorts(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });
        }
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }
}
