using FluxFlow.Components.Storage.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Storage.FileSystem;

public static class FileSystemStorageServiceCollectionExtensions
{
    public static IServiceCollection AddFluxFlowFileSystemStorage(
        this IServiceCollection services,
        string name,
        Action<FileSystemStorageRegistrationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var normalizedName = name.Trim();
        EnsureFactoryIsNotRegistered(services, normalizedName);

        var registration = new FileSystemStorageRegistrationBuilder();
        configure(registration);
        var options = registration.CreateOptions(normalizedName);

        services.AddKeyedSingleton<IStorageStoreFactory>(
            normalizedName,
            (_, _) => new FileSystemStorageStoreFactory(options));

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
                $"File-system storage store factory '{name}' is already registered.");
        }
    }
}
