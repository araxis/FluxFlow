using FluxFlow.Configuration.Components.RequestReply;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.RequestReply;

public sealed class RequestReplyDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "requestReply", DisplayName = "Request–Reply", Category = "Resilience",
        PreferredNodeName = "request-reply", DefaultColor = "#62b6aa",
        Icon = """<path d="M4 7h13l-3-3 1.4-1.4L21.8 9l-6.4 6.4L14 14l3-3H4V7zm16 10H7l3 3-1.4 1.4L2.2 15l6.4-6.4L10 10l-3 3h13v4z"/>""",
        Summary = "Coordinates requests with ACK/NACK feedback and bounded concurrency.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input },
            new() { Name = "ACK", Kind = PortKind.Input }, new() { Name = "NACK", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Request", Kind = PortKind.Output }, new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "requestReply",
        Sections = [new()
        {
            Name = "Coordination", Options =
            [
                Create(nameof(RequestReplyConfiguration.MaxInFlight), "Maximum in flight", OptionEditorKind.Integer,
                    "Maximum requests awaiting feedback. Use 1 for sequential processing. Together with Buffer capacity, must not exceed 2147483647.", 1) with { Min = 1, Max = int.MaxValue, MaxMinusField = nameof(RequestReplyConfiguration.BufferCapacity) },
                Create(nameof(RequestReplyConfiguration.BufferCapacity), "Buffer capacity", OptionEditorKind.Integer,
                    "Additional accepted messages waiting for a processing slot. Together with Maximum in flight, must not exceed 2147483647. A full input postpones admission.", 128) with { Min = 0, Max = int.MaxValue, MaxMinusField = nameof(RequestReplyConfiguration.MaxInFlight) },
                Create(nameof(RequestReplyConfiguration.ReplyTimeout), "Reply timeout", OptionEditorKind.Duration,
                    "Maximum wait for each dispatched request. Timeout does not cancel downstream work.", TimeSpan.FromSeconds(30)) with
                    { MinDuration = TimeSpan.FromTicks(1), MaxDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1) },
                Create(nameof(RequestReplyConfiguration.TimeoutBehavior), "On timeout", OptionEditorKind.Select,
                    "Stop dispatching returns NotDispatched for further work until rerun. Continue can overlap work that timed out.", "StopDispatch") with
                {
                    Choices = [new() { Value = "StopDispatch", DisplayName = "Stop dispatching" }, new() { Value = "Continue", DisplayName = "Continue" }]
                }
            ]
        }]
    };
}
