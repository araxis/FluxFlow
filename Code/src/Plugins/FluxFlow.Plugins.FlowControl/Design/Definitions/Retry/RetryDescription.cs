using FluxFlow.Configuration.Components.Resilience;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Retry;

public sealed class RetryDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "retry", DisplayName = "Retry", Category = "Resilience",
        PreferredNodeName = "retry",
        Icon = """<g><rect fill="none" height="24" width="24"/><rect fill="none" height="24" width="24"/><rect fill="none" height="24" width="24"/></g><g><g/><path d="M12,5V1L7,6l5,5V7c3.31,0,6,2.69,6,6s-2.69,6-6,6s-6-2.69-6-6H4c0,4.42,3.58,8,8,8s8-3.58,8-8S16.42,5,12,5z"/></g>""",
        DefaultColor = "#62b6aa",
        Summary = "Retries transient request failures. Connect Request to an operation and its response to Reply.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }, new() { Name = "Reply", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Request", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "retry",
        PrimaryEditor = nameof(RetryConfiguration.ShouldRetry),
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(RetryConfiguration.AttemptTimeout), "Attempt timeout", OptionEditorKind.Duration,
                    "Maximum time for one attempt. -00:00:00.001 disables the timeout.", defaultValue: TimeSpan.FromSeconds(30)) with
                    { MinDuration = TimeSpan.FromTicks(1), MaxDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1), AllowInfiniteDuration = true },
                Create(nameof(RetryConfiguration.MaxRetryAttempts), "Maximum retries", OptionEditorKind.Integer,
                    "Number of retries after the first attempt. Must be at least one.", defaultValue: 3) with { Min = 1, Max = int.MaxValue },
                Create(nameof(RetryConfiguration.Delay), "Delay", OptionEditorKind.Duration,
                    "Base delay between retry attempts, from zero to one day.", defaultValue: TimeSpan.FromSeconds(2)) with
                    { MinDuration = TimeSpan.Zero, MaxDuration = TimeSpan.FromDays(1) },
                Create(nameof(RetryConfiguration.BackoffType), "Backoff type", OptionEditorKind.Select,
                    "How the retry delay grows.", defaultValue: "Constant") with { Choices = [new() { Value = "Constant", DisplayName = "Constant" }, new() { Value = "Linear", DisplayName = "Linear" }, new() { Value = "Exponential", DisplayName = "Exponential" }] },
                Create(nameof(RetryConfiguration.UseJitter), "Use jitter", OptionEditorKind.Toggle,
                    "Randomize retry delays to reduce synchronized retries.", defaultValue: false),
                Create(nameof(RetryConfiguration.MaxDelay), "Maximum delay", OptionEditorKind.Duration,
                    "Optional delay cap, from zero to one day. Clear to remove the cap.") with
                    { MinDuration = TimeSpan.Zero, MaxDuration = TimeSpan.FromDays(1) },
                Create(nameof(RetryConfiguration.Engine), "Engine", OptionEditorKind.Text,
                    "Required only when a custom retry expression is supplied."),
                Create(nameof(RetryConfiguration.ShouldRetry), "Retry expression", OptionEditorKind.Expression,
                    "Optional Boolean expression evaluated against the reply. Preview a value or an error. When omitted, transient errors are retried.") with { ExpressionResult = ExpressionResultKind.Boolean },
                Create(nameof(RetryConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

