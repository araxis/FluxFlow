using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Components.Mapping;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Mapping;

public static class MapperRegistrationExtensions
{
    public static IServiceCollection AddMapper(this IServiceCollection services, string? expressionBaseDirectory = null)
    {
        var directory = Path.GetFullPath(expressionBaseDirectory ?? AppContext.BaseDirectory);
        services.AddKeyedSingleton<ComponentRegistration>("mapper", (registrations, section) => Configure(registrations, section, directory));
        return services;
    }

    private static void Configure(IServiceCollection services, IConfigurationSection section, string directory)
    {
        var address = section.GetComponentAddress();
        var config = section.ReadRequiredConfiguration<MapperConfiguration>();
        config.Validate();
        var engine = config.Engine;
        services.AddMapperNode(address, engine, provider => provider.GetExpressionEngine(engine)
            .CompileExpression<FlowValue>(config.Expression.Read(directory, engine)), config.HandleErrors, config.OutputCapacity);
        services.AddLinks(address, config);
    }
}
