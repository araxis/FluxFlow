using System.Text.Json;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Json.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Composition;

/// <summary>Applies document resources and embedded contracts to an explicit construction scope.</summary>
public static class ApplicationResourceRegistration
{
    public static void RegisterResources(this ApplicationSetup setup, ApplicationResourceRegistrationContext context,
        IEnumerable<IApplicationResourceRegistrar>? registrars = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(setup.Definition, context.Definition))
            throw new ArgumentException("Resource registration must use the setup's definition.", nameof(context));
        cancellationToken.ThrowIfCancellationRequested();
        var candidateServices = context.Services;
        candidateServices.AddSingleton(
            context.HostServices.GetService<ICompositionProcessingProfileMapper>() ??
            new DefaultCompositionProcessingProfileMapper());
        RegisterProcessingProfiles(setup.Definition, candidateServices);
        var registered = new HashSet<IApplicationResourceRegistrar>(ReferenceEqualityComparer.Instance);
        foreach (var registrar in (registrars ?? context.HostServices.GetServices<IApplicationResourceRegistrar>())
                     .Concat(setup.ResourceContracts.Select(static contract => (IApplicationResourceRegistrar)contract)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (registered.Add(registrar.RegistrationIdentity)) registrar.Register(context);
        }
    }

    private static void RegisterProcessingProfiles(
        ApplicationDefinition definition,
        IServiceCollection services)
    {
        foreach (var (path, instance) in ResourceInstanceDefinition.Enumerate(definition))
            RegisterProcessingProfile(instance, path, services);
    }

    private static void RegisterProcessingProfile(ResourceInstanceDefinition instance, ApplicationAddress path, IServiceCollection services)
    {
        if (!string.Equals(
                instance.Type,
                CompositionProcessingResourceTypes.Profile,
                StringComparison.Ordinal))
        {
            return;
        }

        var profile = JsonSerializer.Deserialize<CompositionProcessingProfile>(
                JsonSerializer.Serialize(instance.Properties),
                ApplicationDefinitionJson.CreateSerializerOptions())
            ?? throw new InvalidOperationException(
                $"Processing profile '{path}' could not be loaded.");
        services.AddFluxFlowResource(
            path,
            _ => profile);
    }
}
