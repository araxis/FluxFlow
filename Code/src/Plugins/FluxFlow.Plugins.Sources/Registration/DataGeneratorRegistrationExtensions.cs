using FluxFlow.Application;
using FluxFlow.Components.DataGeneration;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.DataGeneration;

public static class DataGeneratorRegistrationExtensions
{
    public static IServiceCollection AddDataGenerator(this IServiceCollection services, string? expressionBaseDirectory = null)
    {
        var directory = Path.GetFullPath(expressionBaseDirectory ?? AppContext.BaseDirectory);
        services.AddKeyedSingleton<ComponentRegistration>("dataGenerator", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<DataGeneratorConfiguration>();
            config.Validate();
            registrations.AddDataGeneratorNode(address, () => config.Expression.Read(directory, "jsonata"), config.Locale, config.OutputCapacity);
            registrations.AddLinks(address, config);
        });
        return services;
    }
}
