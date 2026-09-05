using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationEventsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Port_activity_diagnostic_producer_preserves_port_provenance_and_message_correlation(bool isInput)
    {
        await using var signals = new ApplicationRuntimeSignals(logger: null);
        var diagnostics = new BufferBlock<FlowMessage>();
        using var link = signals.Diagnostics.LinkTo(diagnostics);
        var publisher = new ApplicationPortEventPublisher(new Dictionary<ApplicationAddress, ApplicationPortMetadata>(), signals, _ => { });
        var address = ApplicationAddress.WorkflowPort("Orders", "Processor", isInput ? "Input" : "Output");
        var original = FlowMessage.Create("order", correlationId: CorrelationId.New());
        var timestamp = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

        publisher.ReportActivity(new ApplicationPortActivity(timestamp,
            isInput ? ApplicationPortActivityKind.InputAccepted : ApplicationPortActivityKind.OutputEmitted,
            address, null, original.CorrelationId, original.TraceId, original.MessageId));
        var diagnostic = await diagnostics.ReceiveAsync(TimeSpan.FromSeconds(5));

        diagnostic.Headers[FlowEventHeaders.Source].ShouldBe(address.Value);
        diagnostic.Headers[FlowEventHeaders.Workflow].ShouldBe("Orders");
        diagnostic.Headers[FlowEventHeaders.Component].ShouldBe("Processor");
        diagnostic.TraceId.ShouldBe(original.TraceId);
        diagnostic.CorrelationId.ShouldBe(original.CorrelationId);
        diagnostic.CausationId.ShouldBe(original.MessageId);
        diagnostic.MessageId.ShouldNotBe(original.MessageId);
        diagnostic.Timestamp.ShouldBe(timestamp);
        diagnostic.ToFlowEvent().Name.ShouldBe(isInput ? ApplicationDiagnosticNames.InputAccepted : ApplicationDiagnosticNames.OutputEmitted);
    }

    [Fact]
    public async Task Linking_application_events_before_start_does_not_activate_components()
    {
        var activations = 0;
        var services = new ServiceCollection();
        services.AddFluxFlow(Definition("initial"), options => options.StartWithHost = false)
            .Advanced.AddDynamicComponent("event.initial", component => component
                .UseFactory(_ => { activations++; return new EventNode(); })
                .HasOutput("Output", node => node.Output).HasEvents("Events", node => node.Events));
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var target = new BufferBlock<FlowMessage>();
        using var link = application.Events.LinkTo(target);
        activations.ShouldBe(0);
        application.State.ShouldBe(ApplicationState.Empty);
        application.Current.ShouldBeNull();
        application.Events.Completion.IsCompleted.ShouldBeFalse();
        await application.StopAsync();
        application.Events.Completion.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Stable_event_feed_preserves_custom_event_identity_across_changed_port_generations()
    {
        var nodes = new List<EventNode>();
        var services = new ServiceCollection();
        var builder = services.AddFluxFlow(Definition("initial"), options =>
        {
            options.StartWithHost = false;
            options.InitialRevisionId = "rev-1";
            options.Events = new() { Application = "orders" };
        });
        builder.Advanced.AddDynamicComponent("event.initial", component => component
            .UseFactory(_ => CreateNode()).HasOutput("Output", node => node.Output).HasEvents("Events", node => node.Events));
        builder.Advanced.AddDynamicComponent("event.replacement", component => component
            .UseFactory(_ => CreateNode()).HasOutput("ChangedOutput", node => node.Output).HasEvents("Events", node => node.Events));
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var feed = application.Events;
        var received = new BufferBlock<FlowMessage>();
        using var link = feed.LinkTo(received, new DataflowLinkOptions(), IsSample);
        (await application.StartAsync()).IsApplied.ShouldBeTrue();
        var first = CustomEvent("first");
        nodes[0].Emit(first).ShouldBeTrue();
        var before = await received.ReceiveAsync(TimeSpan.FromSeconds(5));
        (await application.ApplyAsync("rev-2", Definition("replacement"))).IsApplied.ShouldBeTrue();
        application.Events.ShouldBeSameAs(feed);
        nodes.Count.ShouldBe(2);
        var second = CustomEvent("second");
        nodes[1].Emit(second).ShouldBeTrue();
        var after = await received.ReceiveAsync(TimeSpan.FromSeconds(5));
        before.Headers[FlowEventHeaders.Revision].ShouldBe("rev-1");
        after.Headers[FlowEventHeaders.Revision].ShouldBe("rev-2");
        before.MessageId.ShouldBe(first.MessageId);
        after.MessageId.ShouldBe(second.MessageId);
        after.TraceId.ShouldBe(second.TraceId);
        after.Headers[FlowEventHeaders.Application].ShouldBe("orders");
        after.Headers[FlowEventHeaders.Workflow].ShouldBe("Main");
        after.Headers[FlowEventHeaders.Component].ShouldBe("Source");
        after.Headers[FlowEventHeaders.Source].ShouldBe("Main.Source");
        after.Headers["custom.category"].ShouldBe("audit");
        after.Value!.ToJsonElement().GetProperty("details").GetProperty("label").GetString().ShouldBe("second");
        application.Current!.RevisionId.ShouldBe("rev-2");

        EventNode CreateNode() { var node = new EventNode(); nodes.Add(node); return node; }
    }

    [Fact]
    public async Task Legacy_events_reach_native_component_activity_application_events_and_existing_activity()
    {
        var node = new EventNode();
        var instance = new ComponentInstance(node, events: node.Events);
        var componentEvents = new BufferBlock<FlowMessage>();
        using var componentLink = instance.Activity.LinkTo(componentEvents);
        var services = new ServiceCollection();
        services.AddFluxFlow(Definition("initial"), options => options.StartWithHost = false)
            .Advanced.AddDynamicComponent("event.initial", component => component.UseInstanceFactory(_ => ValueTask.FromResult(instance)));
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var events = new BufferBlock<FlowMessage>();
        using var eventLink = application.Events.LinkTo(events, new DataflowLinkOptions(), IsSample);
        (await application.StartAsync()).IsApplied.ShouldBeTrue();
        var activity = new BufferBlock<FlowMessage>();
        using var activityLink = application.Ports.Activity.LinkTo(activity);
        var first = CustomEvent("first");
        node.Emit(first).ShouldBeTrue();
        (await events.ReceiveAsync(TimeSpan.FromSeconds(5))).MessageId.ShouldBe(first.MessageId);
        (await componentEvents.ReceiveAsync(TimeSpan.FromSeconds(5))).MessageId.ShouldBe(first.MessageId);
        (await ReadSampleAsync()).MessageId.ShouldBe(first.MessageId);
        componentLink.Dispose();
        var second = CustomEvent("second");
        node.Emit(second).ShouldBeTrue();
        (await events.ReceiveAsync(TimeSpan.FromSeconds(5))).MessageId.ShouldBe(second.MessageId);
        (await ReadSampleAsync()).MessageId.ShouldBe(second.MessageId);
        componentEvents.TryReceive(out _).ShouldBeFalse();
        application.State.ShouldBe(ApplicationState.Running);

        async Task<FlowMessage> ReadSampleAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var message = await activity.ReceiveAsync(timeout.Token);
                if (IsSample(message)) return message;
            }
        }
    }

    [Fact]
    public async Task Revision_events_are_live_before_any_runtime_generation_exists()
    {
        var services = new ServiceCollection();
        services.AddFluxFlow(new ApplicationDefinition(), options =>
        {
            options.StartWithHost = false;
            options.InitialRevisionId = "empty";
        });
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var target = new BufferBlock<FlowMessage>();
        using var link = application.Events.LinkTo(target);
        (await application.StartAsync()).Status.ShouldBe(ApplicationUpdateStatus.Unchanged);
        var proposed = await target.ReceiveAsync(TimeSpan.FromSeconds(5));
        var accepted = await target.ReceiveAsync(TimeSpan.FromSeconds(5));
        new[] { proposed, accepted }.ShouldAllBe(message => message.ToFlowEvent().Name == ApplicationSystemEventNames.RevisionChanged);
        new[] { proposed, accepted }.ShouldAllBe(message => message.Headers[FlowEventHeaders.Revision] == "empty");
        proposed.Value!.ToJsonElement().GetProperty("details").GetProperty("phase").GetString().ShouldBe("Proposed");
        accepted.Value!.ToJsonElement().GetProperty("details").GetProperty("phase").GetString().ShouldBe("Accepted");
        accepted.MessageId.ShouldNotBe(proposed.MessageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_and_drain_events_are_observed_live_with_the_producing_revision(bool legacy)
    {
        var source = new ControlledEventSource();
        var services = new ServiceCollection();
        services.AddFluxFlow(Definition("initial"), options =>
        {
            options.StartWithHost = false;
            options.InitialRevisionId = "lifecycle";
        }).Advanced.AddDynamicComponent("event.initial", component =>
        {
            if (legacy)
                component.UseInstanceFactory(_ => ValueTask.FromResult(new ComponentInstance(source, events: source.Events)));
            else
                component.UseFactory(_ => source).HasEvents("Events", node => node.Events);
        });
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var received = new BufferBlock<FlowMessage>();
        using var link = application.Events.LinkTo(received, new DataflowLinkOptions(),
            message => message.Headers.GetValueOrDefault(FlowEventHeaders.Type)?.StartsWith("source.", StringComparison.Ordinal) == true);
        try
        {
            var starting = application.StartAsync().AsTask();
            try
            {
                var preparation = await received.ReceiveAsync(TimeSpan.FromSeconds(5));
                preparation.ToFlowEvent().Name.ShouldBe("source.starting");
                preparation.Headers[FlowEventHeaders.Revision].ShouldBe("lifecycle");
            }
            finally { source.ReleaseStart.TrySetResult(); }
            (await starting.WaitAsync(TimeSpan.FromSeconds(5))).IsApplied.ShouldBeTrue();
            var stopping = application.StopAsync().AsTask();
            try
            {
                var draining = await received.ReceiveAsync(TimeSpan.FromSeconds(5));
                draining.ToFlowEvent().Name.ShouldBe("source.stopping");
                draining.Headers[FlowEventHeaders.Revision].ShouldBe("lifecycle");
                draining.Headers[FlowEventHeaders.Workflow].ShouldBe("Main");
            }
            finally { source.ReleaseStop.TrySetResult(); }
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            var terminal = await received.ReceiveAsync(TimeSpan.FromSeconds(5));
            terminal.ToFlowEvent().Name.ShouldBe("source.completed");
            terminal.Headers[FlowEventHeaders.Revision].ShouldBe("lifecycle");
            terminal.Headers[FlowEventHeaders.Source].ShouldBe("Main.Source");
            application.Events.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            application.State.ShouldBe(ApplicationState.Stopped);
        }
        finally { source.ReleaseStart.TrySetResult(); source.ReleaseStop.TrySetResult(); }
    }

    private static bool IsSample(FlowMessage message) => message.Headers.GetValueOrDefault(FlowEventHeaders.Type) == "custom.sample";
    private static FlowMessage CustomEvent(string label)
    {
        var original = new FlowEvent { Name = "custom.sample", Kind = FlowEventKind.Audit, Details = FlowValue.From(new { label }) }.ToMessage();
        var headers = original.Headers.ToDictionary(pair => pair.Key, pair => pair.Value);
        headers["custom.category"] = "audit";
        return FlowMessage.Restore(original.Value!, original.MessageId, original.TraceId, original.Timestamp, headers: headers);
    }
    private static ApplicationDefinition Definition(string kind) => ApplicationDefinitionJson.Deserialize($$"""
        { "Resources": {}, "Workflows": { "Main": { "Source": { "Type": "event.{{kind}}" } } } }
        """);

    private sealed class EventNode : FlowNode<string, string>
    {
        public bool Emit(FlowMessage message) => EmitEvent(message);
        protected override Task ProcessAsync(FlowMessage<string> message) => Task.CompletedTask;
    }

    private sealed class ControlledEventSource : IFlowSource
    {
        private readonly BroadcastBlock<FlowMessage> _events = new(static message => message);
        private int _completed;
        public ISourceBlock<FlowMessage> Events => _events;
        public Task Completion => _events.Completion;
        public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            _events.Post(new FlowEvent { Name = "source.starting", Kind = FlowEventKind.Lifecycle }.ToMessage());
            await ReleaseStart.Task.WaitAsync(cancellationToken);
        }
        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0) return;
            _events.Post(new FlowEvent { Name = "source.stopping", Kind = FlowEventKind.Lifecycle }.ToMessage());
            _ = FinishAsync();
        }
        private async Task FinishAsync()
        {
            await ReleaseStop.Task;
            _events.Post(new FlowEvent { Name = "source.completed", Kind = FlowEventKind.Lifecycle }.ToMessage());
            _events.Complete();
        }
        public void Fault(Exception exception) => ((IDataflowBlock)_events).Fault(exception);
        public async ValueTask DisposeAsync() { ReleaseStart.TrySetResult(); ReleaseStop.TrySetResult(); Complete(); await Completion; }
    }
}
