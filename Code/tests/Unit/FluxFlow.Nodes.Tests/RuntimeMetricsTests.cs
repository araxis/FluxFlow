using System.Text.Json;
using System.Diagnostics.Metrics;
using System.Diagnostics;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

[Collection("Source lifecycle diagnostics")]
public sealed class RuntimeMetricsTests
{
    [Fact]
    public void Overlapping_scopes_balance_active_count_without_trace_sampling()
    {
        long active = 0, maximum = 0, completed = 0, exceptions = 0;
        var durations = new List<double>();
        var names = new HashSet<string>();
        Activity? exceptionActivity = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == FlowRuntimeMetrics.MeterName) owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            foreach (var tag in tags) names.Add(tag.Key);
            if (instrument.Name == "fluxflow.processing.active") { active += value; maximum = Math.Max(maximum, active); }
            if (instrument.Name == "fluxflow.processing.completed") completed += value;
            if (instrument.Name == "fluxflow.exceptions") { exceptions += value; exceptionActivity = Activity.Current; }
        });
        listener.SetMeasurementEventCallback<double>((_, value, _, _) => durations.Add(value));
        listener.Start();
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        using var host = new Activity("host").Start();
        using (FlowTracing.StartProcessing(message, this))
        {
            using (FlowTracing.StartProcessing(message, this))
            { }
        }
        FlowDiagnostics.LifecycleException(this, "test", new InvalidOperationException());
        completed.ShouldBe(2);
        maximum.ShouldBe(2);
        active.ShouldBe(0);
        exceptions.ShouldBe(1);
        exceptionActivity.ShouldBeNull();
        Activity.Current.ShouldBeSameAs(host);
        durations.Count.ShouldBe(2);
        durations.ShouldAllBe(value => value >= 0);
        names.ShouldBe(new HashSet<string> { "flow.component.type", "flow.operation", "error.type" }, ignoreOrder: true);
    }

    [Fact]
    public void Throwing_metric_callbacks_do_not_escape_runtime_scopes()
    {
        FlowDiagnostics.AttachComponentAddress(this, "main.throwingListeners");
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == FlowRuntimeMetrics.MeterName) owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("metric observer"));
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => throw new InvalidOperationException("metric observer"));
        listener.Start();
        Should.NotThrow(() =>
        {
            using var operation = FlowTracing.StartProcessing(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), this);
            FlowDiagnostics.LifecycleException(this, "test", new Exception());
        });
    }
}
