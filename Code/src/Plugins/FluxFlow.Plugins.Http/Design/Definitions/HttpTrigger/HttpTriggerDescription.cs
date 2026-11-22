using FluxFlow.Hosting.AspNetCore;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.HttpTrigger;

public sealed class HttpTriggerDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "http.trigger", DisplayName = "HTTP Trigger", Category = "HTTP",
        PreferredNodeName = "http-trigger",
        Icon = """<g><rect fill="none" height="24" width="24"/></g><g><path d="M10,15l5.88,0c0.27-0.31,0.67-0.5,1.12-0.5c0.83,0,1.5,0.67,1.5,1.5c0,0.83-0.67,1.5-1.5,1.5c-0.44,0-0.84-0.19-1.12-0.5 l-3.98,0c-0.46,2.28-2.48,4-4.9,4c-2.76,0-5-2.24-5-5c0-2.42,1.72-4.44,4-4.9l0,2.07C4.84,13.58,4,14.7,4,16c0,1.65,1.35,3,3,3 s3-1.35,3-3V15z M12.5,4c1.65,0,3,1.35,3,3h2c0-2.76-2.24-5-5-5l0,0c-2.76,0-5,2.24-5,5c0,1.43,0.6,2.71,1.55,3.62l-2.35,3.9 C6.02,14.66,5.5,15.27,5.5,16c0,0.83,0.67,1.5,1.5,1.5s1.5-0.67,1.5-1.5c0-0.16-0.02-0.31-0.07-0.45l3.38-5.63 C10.49,9.61,9.5,8.42,9.5,7C9.5,5.35,10.85,4,12.5,4z M17,13c-0.64,0-1.23,0.2-1.72,0.54l-3.05-5.07C11.53,8.35,11,7.74,11,7 c0-0.83,0.67-1.5,1.5-1.5S14,6.17,14,7c0,0.15-0.02,0.29-0.06,0.43l2.19,3.65C16.41,11.03,16.7,11,17,11l0,0c2.76,0,5,2.24,5,5 c0,2.76-2.24,5-5,5c-1.85,0-3.47-1.01-4.33-2.5l2.67,0C15.82,18.82,16.39,19,17,19c1.65,0,3-1.35,3-3S18.65,13,17,13z"/></g>""",
        DefaultColor = "#62b6aa",
        Summary = "Receives requests through the ASP.NET Core host. Acknowledge responds immediately; Reply waits for a correlated message on Response.",
        Inputs = [new() { Name = "Response", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "http.trigger",
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create("Route", "Route", OptionEditorKind.Text,
                    "Endpoint route, for example /telemetry. The runner supplies ASP.NET Core routing and middleware.") with { IsRequired = true },
                Create("Methods", "Methods", OptionEditorKind.Json,
                    "Allowed HTTP methods. Omitted settings use POST; custom method tokens are supported.", defaultValue: new[] { "POST" }),
                Create("ResponseMode", "Response mode", OptionEditorKind.Select,
                    "Acknowledge immediately or wait for a correlated response.", defaultValue: "Acknowledge") with { PreserveChoiceSpelling = true, Choices = [OptionChoiceModel.FromEnum(HttpTriggerResponseMode.Acknowledge), OptionChoiceModel.FromEnum(HttpTriggerResponseMode.Reply)] },
                Create("AcknowledgementStatusCode", "Response status", OptionEditorKind.Integer,
                    "Successful response status (200–299) in Acknowledge mode.", defaultValue: 202) with { VisibleWhen = new("ResponseMode", "Acknowledge"), Min = 200, Max = 299 },
                Create("ReplyTimeout", "Reply timeout", OptionEditorKind.Duration,
                    "Maximum time to wait for a correlated response.", defaultValue: TimeSpan.FromSeconds(30)) with
                    { VisibleWhen = new("ResponseMode", "Reply"), MinDuration = TimeSpan.FromTicks(1), MaxDuration = HttpTriggerConfiguration.MaximumReplyTimeout },
                Create("MaxPendingRequests", "Max pending", OptionEditorKind.Integer,
                    "Maximum requests waiting for a correlated reply.", defaultValue: 1024) with { VisibleWhen = new("ResponseMode", "Reply"), Min = 1, Max = int.MaxValue },
                Create("ShutdownMode", "Shutdown mode", OptionEditorKind.Select,
                    "Cancel returns 503 for pending requests; Drain allows accepted requests to finish within the host shutdown deadline.", defaultValue: "Cancel") with { Choices = [new() { Value = "Cancel", DisplayName = "Cancel" }, new() { Value = "Drain", DisplayName = "Drain" }] },
                Create("MaxRequestBodyBytes", "Max body bytes", OptionEditorKind.Integer,
                    "Maximum request body size in bytes.", defaultValue: 1048576) with { Min = 1, Max = int.MaxValue },
                Create("OutputCapacity", "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}
