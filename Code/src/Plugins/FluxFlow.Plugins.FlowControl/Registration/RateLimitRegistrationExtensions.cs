using FluxFlow.Components.RateLimit;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.RateLimiting;

public static class RateLimitRegistrationExtensions
{
    public static IServiceCollection AddRateLimit(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ComponentRegistration>("rateLimit", Configure);
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadRequiredConfiguration<RateLimitConfiguration>();
        services.AddRateLimitNode(address, config);
        services.AddLinks(address, config);
        services.AddOutputLinks(address.Port("Request"), config.Request);
        services.AddInputLinks(address.Port("Reply"), config.Reply);
    }
}
