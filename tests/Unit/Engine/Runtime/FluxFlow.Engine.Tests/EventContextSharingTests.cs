using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Events;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class EventContextSharingTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Observation_context_shares_unchanged_envelopes_and_preserves_original_metadata(bool error, int context)
    {
        var headers = new Dictionary<string, string>
        {
            [FlowEventHeaders.Application] = "producer", [FlowEventHeaders.Revision] = "initial",
            [FlowEventHeaders.Source] = "Main.Source.Events", ["custom"] = "unchanged"
        };
        var correlation = CorrelationId.New();
        var causation = MessageId.New();
        var original = error
            ? FlowMessage.CreateError(new FlowError("test.failure", "original", "test", false), correlation, headers: headers, causationId: causation)
            : FlowMessage.Create(FlowValue.From(new { value = 42 }), correlation, headers: headers, causationId: causation);
        await using var stream = new ApplicationEventStream(new()
        {
            Application = context == 0 ? null : context == 1 ? "producer" : "consumer"
        });
        var target = new BufferBlock<FlowMessage>();
        using var link = stream.Events.LinkTo(target);
        stream.Publish(original, context == 0 ? null : context == 1 ? "initial" : "next");
        var actual = await target.ReceiveAsync(TimeSpan.FromSeconds(5));
        if (context < 2) actual.ShouldBeSameAs(original);
        else actual.ShouldNotBeSameAs(original);
        actual.MessageId.ShouldBe(original.MessageId);
        actual.TraceId.ShouldBe(original.TraceId);
        actual.Timestamp.ShouldBe(original.Timestamp);
        actual.CorrelationId.ShouldBe(correlation);
        actual.CausationId.ShouldBe(causation);
        actual.IsError.ShouldBe(error);
        if (error) actual.Error.ShouldBeSameAs(original.Error);
        else actual.Value.ShouldBeSameAs(original.Value);
        actual.Headers.Count.ShouldBe(original.Headers.Count);
        actual.Headers[FlowEventHeaders.Application].ShouldBe(context == 2 ? "consumer" : "producer");
        actual.Headers[FlowEventHeaders.Revision].ShouldBe(context == 2 ? "next" : "initial");
        actual.Headers[FlowEventHeaders.Source].ShouldBe("Main.Source.Events");
        actual.Headers["custom"].ShouldBe("unchanged");
        original.Headers[FlowEventHeaders.Application].ShouldBe("producer");
        original.Headers[FlowEventHeaders.Revision].ShouldBe("initial");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Signal_context_preserves_canonical_metadata_and_payload_snapshot(bool workflow)
    {
        using var activity = new Activity("event-source").SetIdFormat(ActivityIdFormat.W3C).Start();
        var dimensions = new Dictionary<string, object?> { ["kind"] = "Input" };
        var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var correlation = CorrelationId.New();
        var causation = MessageId.New();
        var trace = TraceId.New();
        var source = workflow ? ApplicationAddress.WorkflowPort("Main", "Source", "Output") : ApplicationAddress.SystemDiagnostics;
        var message = ApplicationSignalMessage.Create(new FlowEvent
        {
            Name = " context.event ", Timestamp = timestamp, Kind = FlowEventKind.Diagnostic,
            Level = FlowEventLevel.Trace, Dimensions = dimensions
        }, correlation, trace, causation, source);
        dimensions["kind"] = "changed";
        message.MessageId.IsEmpty.ShouldBeFalse();
        message.TraceId.ShouldBe(trace);
        message.CorrelationId.ShouldBe(correlation);
        message.CausationId.ShouldBe(causation);
        message.Timestamp.ShouldBe(timestamp);
        message.Headers["traceparent"].ShouldBe(activity.Id);
        message.Headers[FlowEventHeaders.Type].ShouldBe("context.event");
        message.Headers[FlowEventHeaders.Kind].ShouldBe("diagnostic");
        message.Headers[FlowEventHeaders.Level].ShouldBe("trace");
        message.Headers[FlowEventHeaders.Source].ShouldBe(source.Value);
        message.Headers.ContainsKey(FlowEventHeaders.Workflow).ShouldBe(workflow);
        message.Headers.ContainsKey(FlowEventHeaders.Component).ShouldBe(workflow);
        if (workflow)
        {
            message.Headers[FlowEventHeaders.Workflow].ShouldBe("Main");
            message.Headers[FlowEventHeaders.Component].ShouldBe("Source");
        }
        var decoded = message.ToFlowEvent();
        decoded.Name.ShouldBe("context.event");
        decoded.Timestamp.ShouldBe(timestamp);
        decoded.CorrelationId.ShouldBe(correlation);
        decoded.Dimensions["kind"].ShouldBe("Input");
    }
}
