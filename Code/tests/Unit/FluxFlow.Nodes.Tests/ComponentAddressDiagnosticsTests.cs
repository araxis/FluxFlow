using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Nodes.Tests;

[Collection(ProcessingTraceCollection.Name)]
public sealed class ComponentAddressDiagnosticsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(ActivitySamplingResult.None)]
    [InlineData(ActivitySamplingResult.PropagationData)]
    [InlineData(ActivitySamplingResult.AllDataAndRecorded)]
    public async Task Receive_scope_owns_each_delivery_and_propagates_its_parent_without_inheriting_worker_context(ActivitySamplingResult sampling)
    {
        var source = new object();
        var target = new object();
        FlowDiagnostics.AttachComponentAddress(source, "telemetry.receive");
        FlowDiagnostics.AttachComponentAddress(target, "telemetry.request");
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = item => item.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
            ActivityStopped = spans.Add
        };
        ActivitySource.AddActivityListener(listener);
        using var worker = new Activity("transport worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        var traces = new List<ActivityTraceId>();
        for (var delivery = 0; delivery < 2; delivery++)
        {
            Activity? receive;
            FlowMessage message;
            using (FlowTracing.StartReceiving(source))
            {
                receive = Activity.Current;
                message = Message(delivery);
                await Task.Yield();
                Activity.Current.ShouldBe(receive);
                if (sampling == ActivitySamplingResult.None)
                {
                    receive.ShouldBeNull();
                    message.Headers.ContainsKey("traceparent").ShouldBeFalse();
                }
                else
                {
                    receive.ShouldNotBeNull();
                    receive.OperationName.ShouldBe("telemetry.receive");
                    receive.ParentId.ShouldBeNull();
                    receive.Kind.ShouldBe(ActivityKind.Consumer);
                    receive.TraceId.ShouldNotBe(worker.TraceId);
                    traces.ShouldNotContain(receive.TraceId);
                    traces.Add(receive.TraceId);
                }
            }
            Activity.Current.ShouldBeSameAs(worker);
            using (FlowTracing.StartProcessing(message, target))
            {
                if (receive is not null)
                {
                    Activity.Current!.ParentSpanId.ShouldBe(receive.SpanId);
                    Activity.Current.TraceId.ShouldBe(receive.TraceId);
                    Activity.Current.OperationName.ShouldBe("telemetry.request");
                }
            }
            Activity.Current.ShouldBeSameAs(worker);
        }
        if (sampling == ActivitySamplingResult.AllDataAndRecorded)
            spans.Where(item => item.OperationName == "telemetry.receive").ShouldAllBe(item =>
                Equals(item.GetTagItem(FlowDiagnostics.ComponentAddressTag), "telemetry.receive"));
    }

    [Theory]
    [InlineData(ActivitySamplingResult.None)]
    [InlineData(ActivitySamplingResult.PropagationData)]
    [InlineData(ActivitySamplingResult.AllDataAndRecorded)]
    public async Task Same_type_components_keep_distinct_addresses_across_probes_traces_metrics_and_failures(ActivitySamplingResult sampling)
    {
        var spans = new ConcurrentQueue<Activity>();
        var startedAddresses = new ConcurrentQueue<object?>();
        using var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => sampling,
            ActivityStarted = activity => startedAddresses.Enqueue(activity.GetTagItem(FlowDiagnostics.ComponentAddressTag)),
            ActivityStopped = spans.Enqueue
        };
        ActivitySource.AddActivityListener(activities);
        var measurements = new ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FlowRuntimeMetrics.MeterName) listener.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value))));
        meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value))));
        meter.Start();
        using var diagnostics = new Diagnostics();
        var addresses = new[] { ComponentAddress.Parse("main.first"), ComponentAddress.Parse("other.first") };
        var services = new ServiceCollection();
        foreach (var address in addresses)
            services.AddComponentFactory(address, _ => new Node())
                .AddInputPort<Node>(address, "Input", node => node.Input)
                .AddOutputPort<Node>(address, "Output", node => node.Output)
                .AddEventPort<Node>(address, node => node.Events);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var nodes = addresses.Select(address => (Node)app.GetComponent(address)).ToArray();
        using var firstOutput = provider.ObserveOutput(addresses[0].Port("Output"));
        using var secondOutput = provider.ObserveOutput(addresses[1].Port("Output"));
        using var firstEvents = provider.ObserveOutput(addresses[0].Port("Events"));
        using var secondEvents = provider.ObserveOutput(addresses[1].Port("Events"));
        await app.StartAsync();
        var inputs = new[] { Message(1), Message(2) };
        await Task.WhenAll(nodes[0].Input.SendAsync(inputs[0]), nodes[1].Input.SendAsync(inputs[1]));
        foreach (var (probe, events, index) in new[] { (firstOutput, firstEvents, 0), (secondOutput, secondEvents, 1) })
        {
            probe.Address.ComponentAddress.ShouldBe(addresses[index]);
            var output = await probe.Messages.ReceiveAsync(Timeout);
            output.CausationId.ShouldBe(inputs[index].MessageId);
            output.TraceId.ShouldBe(inputs[index].TraceId);
            output.Headers.ContainsKey(FlowDiagnostics.ComponentAddressTag).ShouldBeFalse();
            var observedEvent = await events.Messages.ReceiveAsync(Timeout);
            observedEvent.Headers[FlowEventHeaders.Source].Json.GetString().ShouldBe(addresses[index].Value);
        }
        var failures = new[] { Message(-1), Message(-2) };
        await Task.WhenAll(nodes[0].Input.SendAsync(failures[0]), nodes[1].Input.SendAsync(failures[1]));
        (await firstOutput.Messages.ReceiveAsync(Timeout)).IsError.ShouldBeTrue();
        (await secondOutput.Messages.ReceiveAsync(Timeout)).IsError.ShouldBeTrue();
        await app.StopAsync().WaitAsync(Timeout);
        for (var index = 0; index < nodes.Length; index++)
        {
            FlowDiagnostics.LifecycleException(nodes[index], "test.stop", nodes[index].Failure);
            var processing = diagnostics.Values.OfType<FlowProcessingException>()
                .Single(item => item.ComponentAddress == addresses[index].Value);
            processing.Exception.ShouldBeSameAs(nodes[index].Failure);
            processing.MessageId.ShouldBe(failures[index].MessageId);
            diagnostics.Values.OfType<FlowLifecycleException>()
                .Single(item => item.ComponentAddress == addresses[index].Value).Exception.ShouldBeSameAs(nodes[index].Failure);
            var own = measurements.Where(item => Equals(item.Tags[FlowDiagnostics.ComponentAddressTag], addresses[index].Value)).ToArray();
            own.Where(item => item.Name == "fluxflow.processing.active").Sum(item => item.Value).ShouldBe(0);
            own.Where(item => item.Name == "fluxflow.processing.completed").Sum(item => item.Value).ShouldBe(2);
            own.Count(item => item.Name == "fluxflow.processing.duration").ShouldBe(2);
            own.Where(item => item.Name == "fluxflow.exceptions").Sum(item => item.Value).ShouldBe(2);
            own.ShouldAllBe(item => Equals(item.Tags["flow.component.type"], typeof(Node).FullName));
            own.SelectMany(item => item.Tags.Keys).ShouldNotContain("flow.message_id");
            if (sampling == ActivitySamplingResult.AllDataAndRecorded)
            {
                spans.Count(span => Equals(span.GetTagItem(FlowDiagnostics.ComponentAddressTag), addresses[index].Value)).ShouldBe(2);
                spans.Where(span => Equals(span.GetTagItem(FlowDiagnostics.ComponentAddressTag), addresses[index].Value))
                    .ShouldAllBe(span => span.OperationName == addresses[index].Value);
                startedAddresses.Count(address => Equals(address, addresses[index].Value)).ShouldBe(2);
                spans.Single(span => Equals(span.GetTagItem("flow.message_id"), failures[index].MessageId.Value))
                    .GetTagItem(FlowDiagnostics.ComponentAddressTag).ShouldBe(addresses[index].Value);
            }
        }
        if (sampling == ActivitySamplingResult.None) spans.ShouldBeEmpty();
        if (sampling == ActivitySamplingResult.PropagationData)
        {
            spans.Count.ShouldBe(4);
            spans.ShouldAllBe(span => !span.TagObjects.Any());
        }
    }

    [Fact]
    public void Control_and_aggregate_scopes_share_the_attached_identity()
    {
        var component = new object();
        FlowDiagnostics.AttachComponentAddress(component, "main.processor");
        Should.Throw<ArgumentException>(() => FlowDiagnostics.AttachComponentAddress(component, "other.processor"));
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add
        };
        ActivitySource.AddActivityListener(listener);
        var message = Message(1);
        using (FlowTracing.StartProcessing(message, component, "Reset")) { }
        using (FlowTracing.StartProcessing([message], component)) { }
        spans.Count.ShouldBe(2);
        spans.ShouldAllBe(span => Equals(span.GetTagItem(FlowDiagnostics.ComponentAddressTag), "main.processor"));
        spans[0].GetTagItem("flow.control.port").ShouldBe("Reset");
        spans[0].OperationName.ShouldBe("main.processor");
        spans[1].OperationName.ShouldBe("main.processor");
        spans[1].GetTagItem("flow.message.count").ShouldBe(1);
    }

    [Fact]
    public void Host_sampler_can_select_processing_control_and_aggregate_traces_by_component_address()
    {
        var selected = new object();
        var ignored = new object();
        FlowDiagnostics.AttachComponentAddress(selected, "main.selected");
        FlowDiagnostics.AttachComponentAddress(ignored, "main.ignored");
        var sampledAddresses = new List<string?>();
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                var address = options.Tags?.FirstOrDefault(tag => tag.Key == FlowDiagnostics.ComponentAddressTag).Value as string;
                sampledAddresses.Add(address);
                return address == "main.selected" ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None;
            },
            ActivityStopped = spans.Add
        };
        ActivitySource.AddActivityListener(listener);
        var message = Message(1);
        foreach (var component in new[] { selected, ignored })
        {
            using (FlowTracing.StartProcessing(message, component)) { }
            using (FlowTracing.StartProcessing(message, component, "Reset")) { }
            using (FlowTracing.StartProcessing([message], component)) { }
        }
        sampledAddresses.ShouldBe(["main.selected", "main.selected", "main.selected", "main.ignored", "main.ignored", "main.ignored"]);
        spans.Count.ShouldBe(3);
        spans.ShouldAllBe(span => Equals(span.GetTagItem(FlowDiagnostics.ComponentAddressTag), "main.selected"));
    }

    [Fact]
    public void Diagnostic_identity_does_not_keep_components_alive()
    {
        var reference = AttachTemporaryComponent();
        for (var attempt = 0; attempt < 3 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        reference.IsAlive.ShouldBeFalse();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AttachTemporaryComponent()
    {
        var component = new object();
        FlowDiagnostics.AttachComponentAddress(component, "main.temporary");
        return new WeakReference(component);
    }

    private static FlowMessage Message(int value) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(value)));

    private sealed class Node : FlowNode
    {
        public readonly Exception Failure = new InvalidOperationException("Original failure", new ArgumentException("Inner failure"));
        public ITargetBlock<FlowMessage> Input { get; }
        public Node()
        {
            Output = new BroadcastBlock<FlowMessage>(static message => message);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, Process, Output));
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }
        private IEnumerable<FlowMessage> Process(FlowMessage message)
        {
            if (message.Value.Json.GetInt32() < 0) throw Failure;
            Events.Post(message);
            return [message.WithValue(message.Value)];
        }
    }

    private sealed class Diagnostics : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        internal ConcurrentQueue<object> Values { get; } = new();
        public Diagnostics() => _discovery = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == FlowDiagnostics.ListenerName) _subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> value) { if (value.Value is { } item) Values.Enqueue(item); }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose()
        {
            _discovery.Dispose();
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}
