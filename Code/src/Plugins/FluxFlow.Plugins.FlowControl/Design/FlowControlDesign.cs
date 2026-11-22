using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.RateLimiting;
using FluxFlow.Configuration.Components.RequestReply;
using FluxFlow.Configuration.Components.Resilience;
using FluxFlow.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.FlowControl;

public sealed class FlowControlDesign : IPluginDesign
{
    public void ConfigureRemoteServices(IServiceCollection services) =>
        services.AddSingleton<IResourceEnvironmentAdapter, TokenBucketEnvironmentAdapter>();

    public PropertyRules Component(PluginComponent component) => component.Palette.ComponentType switch
    {
        "rateLimit" => new() { Validate = s => Read<RateLimitConfiguration>(s).Validate() },
        "requestReply" => new() { Validate = s => Read<RequestReplyConfiguration>(s).Validate() },
        "retry" => new()
        {
            Validate = s => Read<RetryConfiguration>(s).CreatePolicy(),
            ValidationNote = s => s.GetSection("ShouldRetry").Exists()
                ? new("Not validated: expression engine resolution, file access and compilation require the runner.") : null
        },
        _ => throw new ArgumentException("Unknown flow control component.", nameof(component))
    };

    public PropertyRules Connection(PluginConnection connection) => connection.Type == TokenBucketConfiguration.ResourceType
        ? new() { Validate = settings => (settings.Get<TokenBucketConfiguration>() ?? new()).Validate() }
        : throw new ArgumentException("Unknown flow control resource.", nameof(connection));

    private static T Read<T>(IConfiguration settings) where T : class, new() =>
        settings is IConfigurationSection section ? section.ReadRequiredConfiguration<T>()
            : settings.Get<T>() ?? throw new ArgumentException("Missing component settings.");
}
