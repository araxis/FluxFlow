using FluxFlow.Composition;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Hosting.Snapshots;
using FluxFlow.Engine.Ports;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimePreparation(
    ApplicationRuntimePlanFactory plans,
    ApplicationRuntimePortSurfaceFactory portSurfaces,
    ApplicationRuntimeResourceSnapshotFactory resourceSnapshots,
    ApplicationRuntimeComponentActivator componentActivator)
{
    internal async ValueTask<IApplicationRevisionCandidate> PrepareAsync(
        ApplicationRevisionPreparationContext context,
        ApplicationRuntimePortGeneration? currentGeneration,
        Func<ApplicationRuntimePortGeneration, ValueTask> adoptGeneration,
        CancellationToken cancellationToken)
    {
        var definition = context.Plan.Next;
        var plan = plans.Create(definition);
        var snapshots = new List<CompositionServiceProviderSnapshot>();
        var components = new Dictionary<ApplicationRuntimeComponentKey, ApplicationRuntimeBuiltComponent>();
        CompositionRuntime? runtime = null;
        ApplicationRuntimePortGeneration? generation = null;
        ApplicationPortRevision? portRevision = null;
        var releaseGeneration = false;

        try
        {
            var resourceSnapshot = resourceSnapshots.Create(
                definition,
                context,
                cancellationToken);
            snapshots.Add(resourceSnapshot);

            await componentActivator.PopulateAsync(
                    definition,
                    resourceSnapshot.Services,
                    components,
                    cancellationToken)
                .ConfigureAwait(false);

            ApplicationRuntimePortBinder.AddWorkflowSnapshots(
                definition,
                context.RevisionId,
                components,
                snapshots);

            var descriptors = components.Values
                .Select(static value => value.Descriptor)
                .ToArray();
            runtime = CompositionRuntime.Create(descriptors, [], descriptors);

            generation = currentGeneration is not null &&
                ApplicationRuntimePortSurfaceFactory.IsSame(currentGeneration.Surface, plan.Surface)
                    ? currentGeneration.Acquire()
                    : new ApplicationRuntimePortGeneration(
                        portSurfaces.Create(plan.Surface),
                        plan.Surface);
            releaseGeneration = true;

            await using (var revisionBuilder = generation.Ports.CreateRevision(context.RevisionId))
            {
                ApplicationRuntimePortBinder.ConfigureRevision(revisionBuilder, components);
                revisionBuilder.SetLinks(plan.Links);
                portRevision = revisionBuilder.Build();
            }

            return new ApplicationRuntimeRevisionCandidate(
                runtime,
                portRevision,
                snapshots,
                generation.ReleaseAsync,
                ReferenceEquals(generation, currentGeneration)
                    ? null
                    : () => adoptGeneration(generation));
        }
        catch (Exception preparationFailure)
        {
            var cleanupFailures = new List<Exception>();
            if (portRevision is not null)
            {
                try
                {
                    await portRevision.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception);
                }
            }

            try
            {
                if (runtime is not null)
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await ApplicationRuntimeCleanup.DisposeComponentsAsync(
                            components.Values.Select(static value => value.Descriptor))
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            try
            {
                await ApplicationRuntimeCleanup.DisposeSnapshotsAsync(snapshots).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            if (releaseGeneration && generation is not null)
            {
                try
                {
                    await generation.ReleaseAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception);
                }
            }

            if (cleanupFailures.Count == 0)
                throw;

            cleanupFailures.Insert(0, preparationFailure);
            throw new AggregateException(
                "Canonical application runtime preparation and cleanup failed.",
                cleanupFailures);
        }
    }
}
