using FluxFlow.Environments;
using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.HttpClient;
using FluxFlow.Authoring.Definitions.HttpTrigger;
using FluxFlow.Configuration.Components.Http;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Hosting.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Plugins.Http;

public sealed class HttpPlugin : IFluxFlowPlugin
{
    public PluginDescription Description { get; } = new("FluxFlow.Plugins.Http",
        [Describe(new HttpClientDescription()), Describe(new HttpTriggerDescription())],
        [new("http.client", "HTTP", "BaseAddress", false,
            JsonSerializer.SerializeToElement(new { Type = "http.client", BaseAddress = "http://localhost:8080/" }), [])])
    { Design = new(typeof(HttpDesign).Assembly.GetName().Name!, typeof(HttpDesign).FullName!,
        ["Polly.Core", "Duende.IdentityModel", "FluxFlow.Plugins.Http.Settings"]) };

    public void ConfigureHost(IServiceCollection services)
    {
        new HttpDesign().ConfigureRemoteServices(services);
        services.AddSingleton(WireMockContainer.Definition);
    }

    public void ConfigureWorkflow(IServiceCollection services) => services.AddHttp().AddHttpTriggers();

    public void ConfigureWorkflowHost(IHostBuilder host, IConfiguration configuration)
    {
        var hasTriggers = configuration.GetSection("Workflows").GetChildren()
            .SelectMany(workflow => workflow.GetChildren())
            .Any(component => string.Equals(component["Type"], "http.trigger", StringComparison.OrdinalIgnoreCase));
        if (!hasTriggers) return;

        host.UseFluxFlowHttpTriggers();
    }

    private static PluginComponent Describe(IComponentDescription component) =>
        new(component.Palette, component.Inspector, component.Documentation, "properties");
}
