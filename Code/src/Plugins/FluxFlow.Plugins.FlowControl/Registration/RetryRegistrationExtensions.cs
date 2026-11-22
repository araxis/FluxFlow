using FluxFlow.Components.Resilience;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Expressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Resilience;

public static class RetryRegistrationExtensions
{
    public static IServiceCollection AddRetry(this IServiceCollection services, string? expressionBaseDirectory = null)
    {
        var directory = Path.GetFullPath(expressionBaseDirectory ?? AppContext.BaseDirectory);
        services.AddKeyedSingleton<ComponentRegistration>("retry", (registrations, section) => Configure(registrations, section, directory));
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section, string directory)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadRequiredConfiguration<RetryConfiguration>();
        services.AddRetryNode(address, config, provider => provider.GetExpressionEngine(config.Engine)
            .CompileExpression<bool>(config.ShouldRetry!.Read(directory, config.Engine)));
        services.AddLinks(address, config);
        services.AddOutputLinks(address.Port("Request"), config.Request);
        services.AddInputLinks(address.Port("Reply"), config.Reply);
    }
}
