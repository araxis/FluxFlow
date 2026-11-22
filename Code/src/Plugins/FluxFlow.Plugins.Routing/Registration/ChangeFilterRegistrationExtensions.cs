using FluxFlow.Application;
using FluxFlow.Components.ChangeFilter;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Core.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Filtering;

public static class ChangeFilterRegistrationExtensions
{
    public static IServiceCollection AddChangeFilter(this IServiceCollection services, string? expressionBaseDirectory = null)
    {
        var directory = Path.GetFullPath(expressionBaseDirectory ?? AppContext.BaseDirectory);
        services.AddKeyedSingleton<ComponentRegistration>("changeFilter", (registrations, section) => Configure(registrations, section, directory));
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section, string directory)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadRequiredConfiguration<ChangeFilterConfiguration>();
        config.Validate();
        services.AddChangeFilterNode(address, provider => provider.GetExpressionEngine(config.Engine)
            .CompileExpression<string>(config.KeySelector.Read(directory, config.Engine)),
            config.IgnoreCase, config.OutputCapacity);
        services.AddLinks(address, config);
    }
}
