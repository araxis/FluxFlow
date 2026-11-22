using FluxFlow.Components.RateLimit;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.RequestReply;

public static class RequestReplyRegistrationExtensions
{
    public static IServiceCollection AddRequestReply(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ComponentRegistration>("requestReply", Configure);
        services.AddKeyedSingleton<ComponentConfigurationCheck>("requestReply", (_, section) =>
            section.ReadRequiredConfiguration<RequestReplyConfiguration>().Validate());
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadRequiredConfiguration<RequestReplyConfiguration>();
        services.AddRequestReplyNode(address, config);
        services.AddLinks(address, config);
        services.AddOutputLinks(address.Port("Request"), config.Request);
        services.AddInputLinks(address.Port("ACK"), config.ACK);
        services.AddInputLinks(address.Port("NACK"), config.NACK);
    }
}
