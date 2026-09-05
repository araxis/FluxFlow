using Xunit;

namespace FluxFlow.Workflow.ExternalSmokeTests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ExternalSmokeFactAttribute : FactAttribute
{
    internal const string EnabledVariable = "FLUXFLOW_EXTERNAL_SMOKE";

    public ExternalSmokeFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnabledVariable),
                "1",
                StringComparison.Ordinal))
        {
            Skip = $"Set {EnabledVariable}=1 to run external workflow smoke tests.";
        }
    }
}
