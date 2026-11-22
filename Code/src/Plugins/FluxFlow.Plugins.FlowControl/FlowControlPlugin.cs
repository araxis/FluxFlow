using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.RateLimit;
using FluxFlow.Authoring.Definitions.RequestReply;
using FluxFlow.Authoring.Definitions.Retry;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.RateLimiting;
using FluxFlow.Configuration.Components.RequestReply;
using FluxFlow.Configuration.Components.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.FlowControl;

public sealed class FlowControlPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.FlowControl",
        [Describe(new RateLimitDescription()), Describe(new RequestReplyDescription()), Describe(new RetryDescription())], [TokenBucketDescription.Describe()])
    { Design = new(typeof(FlowControlDesign).Assembly.GetName().Name!, typeof(FlowControlDesign).FullName!, ["Polly.Core", "FluxFlow.Plugins.FlowControl.Settings"]) };

    public void ConfigureWorkflow(IServiceCollection services)
    {
        services.AddTokenBucketResource().AddRateLimit().AddRequestReply().AddRetry();
        services.AddKeyedSingleton<ComponentConfigurationCheck>("rateLimit", (configuration, section) =>
        {
            var config = section.ReadRequiredConfiguration<RateLimitConfiguration>();
            config.Validate();
            var workflow = section.GetComponentAddress().WorkflowName;
            var paths = ApplicationAddress.ResolveResourcePaths(config.Limiter, workflow);
            if (paths[0].WorkflowName is { } owner && owner != workflow)
                throw new ArgumentException($"Rate limiter '{paths[0]}' belongs to another workflow. Use an application resource for sharing.");
            if (!paths.Any(path => configuration.GetSection((path.WorkflowName is null ? "" : "Workflows:") +
                    string.Join(':', path.Segments))["Type"] == TokenBucketConfiguration.ResourceType))
                throw new ArgumentException($"Rate limiter '{paths[0]}' is not configured. Add a Rate limiter resource and select it, or use a custom host that supplies this reference.");
        });
    }

    public void ConfigureHost(IServiceCollection services) => new FlowControlDesign().ConfigureRemoteServices(services);

    private static PluginComponent Describe(IComponentDescription component) =>
        new(component.Palette, component.Inspector, component.Documentation);
}
