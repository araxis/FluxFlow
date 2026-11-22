using FluxFlow.Application;
using FluxFlow.Components.Http;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Http;

public static class HttpRegistrationExtensions
{
    /// <summary>Registers http.client resources and components. Neither registration nor construction sends requests.</summary>
    public static IServiceCollection AddHttp(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ResourceRegistration>("http.client", (registrations, address, section) =>
        {
            var config = section.Get<HttpClientConfiguration>() ?? throw new ArgumentException($"Missing HTTP settings at '{section.Path}'.");
            registrations.AddHttpConnection(address.Value, config);
        });
        services.AddKeyedSingleton<ComponentRegistration>("http.client", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<HttpComponentConfiguration>();
            registrations.AddHttpClientNode(address, config);
            registrations.AddLinks(address, config);
        });
        return services;
    }
}
