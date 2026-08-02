namespace FluxFlow.Components.Expectations;

/// <summary>Stable normal-result kinds emitted by the canonical expectation node.</summary>
public static class ExpectationResultKinds
{
    public const string Matched = "Matched";
    public const string Unmet = "Unmet";
    public const string TimedOut = "TimedOut";
    public const string Completed = "Completed";
    public const string EvaluationFailed = "EvaluationFailed";
}
