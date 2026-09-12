using FluxFlow.Composition;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Engine.Internal.Snapshots;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeResourceSnapshotFactory(
    IServiceProvider hostServices,
    IReadOnlyList<IApplicationResourceRegistrar> registrars)
{
    internal CompositionServiceProviderSnapshot Create(ApplicationSetup setup,
        ApplicationRevisionPreparationContext context, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        setup.RegisterResources(new ApplicationResourceRegistrationContext(setup.Definition,
            context.Sequence, context.RevisionId, hostServices, services), registrars, cancellationToken);
        return new CompositionServiceProviderSnapshotBuilder().AddServices(services).Build(
            CompositionProviderBoundary.ResourceRevision, $"resources:{context.RevisionId}", fallbackProvider: hostServices);
    }
}
