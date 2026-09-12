using FluxFlow.Composition;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Engine.Internal.Snapshots;
using FluxFlow.Engine.Ports;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimePreparation(
    ApplicationRuntimePlanFactory plans,
    ApplicationRuntimePortSurfaceFactory portSurfaces,
    ApplicationRuntimeResourceSnapshotFactory resourceSnapshots,
    ApplicationRuntimeComponentActivator componentActivator,
    Events.ApplicationEventStream events)
{
    internal async ValueTask<IApplicationRevisionCandidate> PrepareAsync(
        ApplicationRevisionPreparationContext context,
        ApplicationRuntimePortGeneration? currentGeneration,
        Func<ApplicationRuntimePortGeneration, ValueTask> adoptGeneration,
        CancellationToken cancellationToken)
    {
        var setup = context.Plan.Next;
        var definition = setup.Definition;
        var plan = plans.Create(setup);
        var snapshots = new List<CompositionServiceProviderSnapshot>();
        var components = new Dictionary<ApplicationRuntimeComponentKey, ApplicationRuntimeBuiltComponent>();
        ApplicationRuntimePortGeneration? generation = null;
        ApplicationPortRevision? portRevision = null;
        Events.ApplicationEventAttachment? eventLink = null;
        var releaseGeneration = false;

        try
        {
            var resourceSnapshot = resourceSnapshots.Create(
                setup,
                context,
                cancellationToken);
            snapshots.Add(resourceSnapshot);

            await componentActivator.PopulateAsync(
                    definition,
                    plan.Catalog,
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
                .Select(static value => value.Instance)
                .ToArray();
            var drainPlan = ApplicationRuntimeDrainPlan.TryCreate(plan.Links, components);

            generation = currentGeneration is not null &&
                ApplicationRuntimePortSurfaceFactory.IsSame(currentGeneration.Surface, plan.Surface)
                    ? currentGeneration.Acquire()
                    : new ApplicationRuntimePortGeneration(
                        portSurfaces.Create(plan.Surface),
                        plan.Surface);
            releaseGeneration = true;
            generation.Ports.ForwardEventsTo(events);
            eventLink = new Events.ApplicationEventAttachment(events, context.RevisionId, descriptors,
                generation.Ports.PublishActivity);

            await using (var revisionBuilder = generation.Ports.CreateRevision(context.RevisionId))
            {
                ApplicationRuntimePortBinder.ConfigureRevision(revisionBuilder, components);
                revisionBuilder.SetLinks(plan.Links, plan.DiscardedOutputs, plan.ReplyOutputs);
                portRevision = revisionBuilder.Build();
            }

            return new ApplicationRuntimeRevisionCandidate(
                descriptors,
                portRevision,
                snapshots,
                drainPlan,
                generation.ReleaseAsync,
                ReferenceEquals(generation, currentGeneration)
                    ? null
                    : () => adoptGeneration(generation),
                eventLink);
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
                await ApplicationRuntimeCleanup.DisposeComponentsAsync(
                        components.Values.Select(static value => value.Instance))
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            try
            {
                if (eventLink is not null) await eventLink.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) { cleanupFailures.Add(exception); }

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
