using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.Logger;
using FluxFlow.Configuration.Components.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Logging;

public sealed class LoggingPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = Describe();

    private static PluginDescription Describe()
    {
        IComponentDescription logger = new LoggerDescription();
        var collector = LoggingDesign.Collector;
        return new("FluxFlow.Plugins.Logging", [new(logger.Palette, logger.Inspector, logger.Documentation, "properties")],
            [new(collector.Type, collector.Name, "Endpoint", false,
                JsonSerializer.SerializeToElement(new { Type = collector.Type, Endpoint = collector.DefaultEndpoint }), [])])
        { Design = new(typeof(LoggingDesign).Assembly.GetName().Name!, typeof(LoggingDesign).FullName!, []) };
    }

    public void ConfigureHost(IServiceCollection services) => new LoggingDesign().ConfigureRemoteServices(services);

    public void ConfigureWorkflow(IServiceCollection services) => services.AddLogger();
}
