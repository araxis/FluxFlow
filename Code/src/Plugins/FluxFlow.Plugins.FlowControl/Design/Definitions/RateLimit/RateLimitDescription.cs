using FluxFlow.Configuration.Components.RateLimiting;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.RateLimit;

public sealed class RateLimitDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "rateLimit", DisplayName = "Rate Limit", Category = "Resilience",
        PreferredNodeName = "rate-limit",
        Icon = """<path d="M0 0h24v24H0z" fill="none"/><path d="M20.38 8.57l-1.23 1.85a8 8 0 0 1-.22 7.58H5.07A8 8 0 0 1 15.58 6.85l1.85-1.23A10 10 0 0 0 3.35 19a2 2 0 0 0 1.72 1h13.85a2 2 0 0 0 1.74-1 10 10 0 0 0-.27-10.44z"/><path d="M10.59 15.41a2 2 0 0 0 2.83 0l5.66-8.49-8.49 5.66a2 2 0 0 0 0 2.83z"/>""",
        DefaultColor = "#62b6aa",
        Summary = "Limits request delivery using a host-owned RateLimiter resource; responses enter Reply.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }, new() { Name = "Reply", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Request", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "rateLimit",
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(RateLimitConfiguration.Limiter), "Limiter", OptionEditorKind.Text,
                    "Select a rate limiter resource. Existing host-provided references remain supported.") with
                    { IsRequired = true, RelatedResource = TokenBucketConfiguration.ResourceType },
                Create(nameof(RateLimitConfiguration.ResponseTimeout), "Response timeout", OptionEditorKind.Duration,
                    "Maximum wait for a correlated reply. -00:00:00.001 means no timeout.", defaultValue: Timeout.InfiniteTimeSpan) with
                    { MinDuration = TimeSpan.FromTicks(1), MaxDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1), AllowInfiniteDuration = true },
                Create(nameof(RateLimitConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

