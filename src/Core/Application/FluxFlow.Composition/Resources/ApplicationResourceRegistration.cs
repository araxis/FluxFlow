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
        RegisterProcessingProfiles(setup.Definition.Resources, candidateServices);
        var registered = new HashSet<IApplicationResourceRegistrar>(ReferenceEqualityComparer.Instance);
        foreach (var registrar in (registrars ?? context.HostServices.GetServices<IApplicationResourceRegistrar>())
                     .Concat(setup.ResourceContracts.Select(static contract => (IApplicationResourceRegistrar)contract)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (registered.Add(registrar.RegistrationIdentity)) registrar.Register(context);
        }
    }

    private static void RegisterProcessingProfiles(
        IReadOnlyDictionary<string, ResourceDefinition> resources,
        IServiceCollection services)
    {
        foreach (var (name, resource) in resources)
            RegisterProcessingProfile(resource, [name], services);
    }

    private static void RegisterProcessingProfile(
        ResourceDefinition resource,
        IReadOnlyList<string> path,
        IServiceCollection services)
    {
        if (resource is ResourceGroupDefinition group)
        {
            foreach (var (name, child) in group.Resources)
                RegisterProcessingProfile(child, [.. path, name], services);
            return;
        }

        var instance = (ResourceInstanceDefinition)resource;
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
                $"Processing profile '{string.Join('.', path)}' could not be loaded.");
        services.AddFluxFlowResource(
            ApplicationAddress.Resource(path.ToArray()),
            _ => profile);
    }
}
