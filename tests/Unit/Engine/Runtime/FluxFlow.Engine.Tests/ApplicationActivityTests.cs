using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationActivityTests
{
    [Fact]
    public async Task System_event_is_mirrored_to_canonical_activity()
    {
        await using var signals = new ApplicationRuntimeSignals(logger: null);
        using var systemLink = signals.SystemEvents.LinkTo(
            DataflowBlock.NullTarget<FlowMessage>());
        var activity = new BufferBlock<FlowMessage>();
        using var activityLink = signals.Activity.LinkTo(activity);
        var timestamp = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

        var result = await signals.PublishSystemEventAsync(
            ApplicationSignalMessage.Create(new FlowEvent
            {
                Timestamp = timestamp,
                Name = ApplicationSystemEventNames.RevisionChanged,
                Kind = FlowEventKind.Lifecycle,
                Dimensions = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["category"] = "Revision",
                    ["subject"] = "revision-2"
                }
            }),
            CancellationToken.None);

        result.IsAccepted.ShouldBeTrue();
        var message = await activity.ReceiveAsync(TimeSpan.FromSeconds(5));
        message.Headers[FlowEventHeaders.Type].ShouldBe(ApplicationSystemEventNames.RevisionChanged);
        message.ToFlowEvent().Kind.ShouldBe(FlowEventKind.Lifecycle);
        message.ToFlowEvent().Dimensions["subject"].ShouldBe("revision-2");
    }

    [Fact]
    public async Task Component_activity_attachment_forwards_without_changing_identity()
    {
        await using var signals = new ApplicationRuntimeSignals(logger: null);
        var source = new BufferBlock<FlowMessage>();
        var activity = new BufferBlock<FlowMessage>();
        using var aggregateLink = signals.AttachActivity(source);
        using var observerLink = signals.Activity.LinkTo(activity);
        var expected = new FlowEvent
        {
            Name = "message.received",
            Kind = FlowEventKind.Domain
        }.ToMessage();

        await source.SendAsync(expected);

        var actual = await activity.ReceiveAsync(TimeSpan.FromSeconds(5));
        actual.MessageId.ShouldBe(expected.MessageId);
        actual.Headers[FlowEventHeaders.Type].ShouldBe("message.received");
    }
}
