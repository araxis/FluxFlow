using FluxFlow.Components.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Storage.SqlFile;

public static class SqlFileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowSqlFileStorage(
        this IServiceCollection services,
        string name,
        Action<SqlFileStorageRegistrationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var normalizedName = name.Trim();
        EnsureFactoryIsNotRegistered(services, normalizedName);

        var registration = new SqlFileStorageRegistrationBuilder();
        configure(registration);
        var options = registration.CreateOptions(normalizedName);

        services.AddKeyedSingleton<IStorageStoreFactory>(
            normalizedName,
            (_, _) => new SqlFileStorageStoreFactory(options));

        return services;
    }

    private static void EnsureFactoryIsNotRegistered(
        IServiceCollection services,
        string name)
    {
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(IStorageStoreFactory) &&
                descriptor.IsKeyedService &&
                Equals(descriptor.ServiceKey, name)))
        {
            throw new InvalidOperationException(
                $"SQL file storage store factory '{name}' is already registered.");
        }
    }
}
