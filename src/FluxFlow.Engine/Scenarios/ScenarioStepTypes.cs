namespace FluxFlow.Engine.Scenarios;

public static class ScenarioStepTypes
{
    public const string ExpectEvent = "expect.event";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ExpectEvent
    };
}
