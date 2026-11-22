using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Configuration.Components.RateLimiting;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Plugins.FlowControl;

public static class TokenBucketDescription
{
    public static PluginConnection Describe()
    {
        var defaults = JsonSerializer.SerializeToNode(new TokenBucketConfiguration())!.AsObject();
        defaults["Type"] = TokenBucketConfiguration.ResourceType;
        return new(TokenBucketConfiguration.ResourceType, "Rate limiter", null, false, JsonSerializer.SerializeToElement(defaults),
        [
            Create(nameof(TokenBucketConfiguration.TokenLimit), "Burst size", OptionEditorKind.Integer,
                "Maximum accumulated permits. A new run starts with this many permits.", 10) with { Min = 1, Max = int.MaxValue },
            Create(nameof(TokenBucketConfiguration.TokensPerPeriod), "Refill count", OptionEditorKind.Integer,
                "Permits added each interval, up to the burst size. Each dispatched request consumes one permit.", 10) with { Min = 1, Max = int.MaxValue },
            Create(nameof(TokenBucketConfiguration.ReplenishmentPeriod), "Refill interval", OptionEditorKind.Duration,
                "Time between automatic refills. Replies do not return consumed permits.", "00:00:01") with
                { MinDuration = TimeSpan.FromMilliseconds(1), MaxDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1) },
            Create(nameof(TokenBucketConfiguration.QueueLimit), "Queue limit", OptionEditorKind.Integer,
                "Maximum waiting requests shared by components using this resource. Oldest requests dispatch first; zero rejects immediately when no permit is available.", 128) with { Min = 0, Max = int.MaxValue }
        ]) { WorkflowResourcesAreLocal = true };
    }
}
