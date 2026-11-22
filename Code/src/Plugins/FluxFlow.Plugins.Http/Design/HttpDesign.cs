using FluxFlow.Environments;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Hosting.AspNetCore;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Plugins.Http;

public sealed class HttpDesign : IPluginDesign
{
    public void ConfigureRemoteServices(IServiceCollection services) => services.AddSingleton<IResourceEnvironmentAdapter, HttpEnvironmentAdapter>()
        .AddSingleton(WireMock.WireMockMappings.Provider);

    public PropertyRules Connection(PluginConnection connection) => HttpConnectionDescription.Rules;

    public PropertyRules Component(PluginComponent component) => component.Palette.ComponentType switch
    {
        "http.client" => new() { Validate = settings =>
        {
            var config = ((IConfigurationSection)settings).ReadRequiredConfiguration<HttpComponentConfiguration>();
            config.Validate();
        } },
        "http.trigger" => new()
        {
            Validate = settings => HttpTriggerConfiguration.Read((IConfigurationSection)settings),
            ValidationNote = _ => new("Route syntax and endpoint mapping are checked by the ASP.NET Core host."),
            StringList = new("Methods", "Methods", "method", ValidateMethod)
        },
        _ => throw new ArgumentException("Unknown HTTP component.", nameof(component))
    };

    private static string? ValidateMethod(string value)
    {
        try { _ = new HttpMethod(value); return null; }
        catch (ArgumentException) { return "Enter a valid HTTP method, such as GET, POST or a custom token."; }
        catch (FormatException) { return "Enter a valid HTTP method, such as GET, POST or a custom token."; }
    }
}
