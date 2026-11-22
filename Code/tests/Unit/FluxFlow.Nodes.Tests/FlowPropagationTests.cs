using System.Diagnostics;
using System.Text.Json;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

[Collection(ProcessingTraceCollection.Name)]
public sealed class FlowPropagationTests
{
    [Fact]
    public void Standard_carrier_round_trip_preserves_parent_flags_state_and_baggage()
    {
        using var producer = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C);
        producer.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        producer.TraceStateString = "vendor=one";
        producer.AddBaggage("region", "north");
        producer.Start();
        var input = FlowMessage.Create(JsonFlowValue.Null);
        input.Headers["traceparent"].Json.GetString().ShouldBe(producer.Id);
        input.Headers["tracestate"].Json.GetString().ShouldBe("vendor=one");

        ActivityContext extracted = default;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                extracted = options.Parent;
                return ActivitySamplingResult.AllDataAndRecorded;
            }
        };
        ActivitySource.AddActivityListener(listener);
        using (FlowTracing.StartProcessing(input, this))
        {
            extracted.TraceId.ShouldBe(producer.TraceId);
            extracted.SpanId.ShouldBe(producer.SpanId);
            extracted.TraceFlags.ShouldBe(ActivityTraceFlags.Recorded);
            extracted.TraceState.ShouldBe("vendor=one");
            Activity.Current!.ParentSpanId.ShouldBe(producer.SpanId);
            Activity.Current.GetBaggageItem("region").ShouldBe("north");
            var derived = input.WithValue(JsonFlowValue.Null);
            using (FlowTracing.StartProcessing(derived, this))
                Activity.Current!.GetBaggageItem("region").ShouldBe("north");
        }
        Activity.Current.ShouldBeSameAs(producer);
    }

    [Fact]
    public void Replacement_removes_stale_propagator_fields_but_preserves_business_headers()
    {
        FlowMessage input;
        using (var old = new Activity("old").SetIdFormat(ActivityIdFormat.W3C))
        {
            old.TraceStateString = "vendor=old";
            old.AddBaggage("old", "value");
            old.Start();
            input = FlowMessage.Create(JsonFlowValue.Null,
                headers: new Dictionary<string, FlowValue> { ["customer"] = "unchanged" });
        }
        using var next = new Activity("next").SetIdFormat(ActivityIdFormat.W3C).Start();
        var output = input.WithValue(JsonFlowValue.Null);
        output.Headers["customer"].ShouldBeSameAs(input.Headers["customer"]);
        output.Headers["traceparent"].Json.GetString().ShouldBe(next.Id);
        output.Headers.ContainsKey("tracestate").ShouldBeFalse();
        var baggage = DistributedContextPropagator.CreateDefaultPropagator().ExtractBaggage(output.Headers,
            static (object? carrier, string name, out string? value, out IEnumerable<string>? values) =>
            {
                var headers = (IReadOnlyDictionary<string, FlowValue>)carrier!;
                value = headers.TryGetValue(name, out var header) ? header.Json.GetString() : null;
                values = null;
            });
        (baggage?.Any() ?? false).ShouldBeFalse();
    }

    [Fact]
    public void Structured_trace_header_is_ignored_without_falling_back_to_worker_parent()
    {
        using var worker = new Activity("unrelated-worker").SetIdFormat(ActivityIdFormat.W3C).Start();
        var input = FlowMessage.Create(JsonFlowValue.Null, headers: new Dictionary<string, FlowValue>
        {
            ["traceparent"] = new JsonFlowValue(JsonSerializer.SerializeToElement(new { id = 42 }))
        });
        ActivityContext extracted = worker.Context;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FluxFlow.Core",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                extracted = options.Parent;
                return ActivitySamplingResult.AllDataAndRecorded;
            }
        };
        ActivitySource.AddActivityListener(listener);
        using (FlowTracing.StartProcessing(input, this))
            extracted.ShouldBe(default);
        Activity.Current.ShouldBeSameAs(worker);
    }
}
