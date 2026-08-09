using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Engine.Internal.Snapshots;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeResourceSnapshotFactory(
    IServiceProvider hostServices,
    IReadOnlyList<IApplicationResourceRegistrar> registrars)
{
    internal CompositionServiceProviderSnapshot Create(
        ApplicationDefinition definition,
        ApplicationRevisionPreparationContext context,
        CancellationToken cancellationToken)
    {
        var candidateServices = new ServiceCollection();
        candidateServices.AddSingleton(
            hostServices.GetService<ICompositionProcessingProfileMapper>() ??
            new DefaultCompositionProcessingProfileMapper());
        RegisterProcessingProfiles(definition.Resources, candidateServices);

        var registrationContext = new ApplicationResourceRegistrationContext(
            definition,
            context.Sequence,
            context.RevisionId,
            hostServices,
            candidateServices);
        var registered = new HashSet<IApplicationResourceRegistrar>(
            ReferenceEqualityComparer.Instance);
        foreach (var registrar in registrars.Concat(
                     definition.ApplicationResourceContracts.Select(
                         static contract => (IApplicationResourceRegistrar)contract)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = registrar.RegistrationIdentity;
            if (registered.Add(identity))
                registrar.Register(registrationContext);
        }

        return new CompositionServiceProviderSnapshotBuilder()
            .AddServices(candidateServices)
            .Build(
                CompositionProviderBoundary.ResourceRevision,
                $"resources:{context.RevisionId}",
                fallbackProvider: hostServices);
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
            ?? throw new ApplicationRuntimeAssemblerException(
                $"Processing profile '{string.Join('.', path)}' could not be loaded.");
        services.AddFluxFlowResource(
            ApplicationAddress.Resource(path.ToArray()),
            _ => profile);
    }
}
