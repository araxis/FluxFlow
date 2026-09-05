using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluxFlow.Engine.HealthChecks;

internal sealed class FluxFlowApplicationHealthCheck(FluxFlowApplication? application)
    : IHealthCheck
{
    private const string HealthyDescription =
        "An active FluxFlow application revision is available.";
    private const string DegradedDescription =
        "The active FluxFlow application revision remains available after the latest update was rejected.";
    private const string MissingDescription =
        "FluxFlow application services are not registered.";
    private const string UnavailableDescription =
        "The FluxFlow application has no active ready revision.";
    private const string StoppedDescription =
        "The FluxFlow application is stopped and is not ready.";

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (application is null)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                MissingDescription,
                exception: null,
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["applicationState"] = "Unavailable"
                }));
        }

        var stateBefore = application.State;
        var currentBefore = application.Current;
        var lastUpdateBefore = application.LastUpdate;
        var lastUpdateAfter = application.LastUpdate;
        var currentAfter = application.Current;
        var stateAfter = application.State;
        if (stateBefore != stateAfter ||
            !ReferenceEquals(currentBefore, currentAfter) ||
            !ReferenceEquals(lastUpdateBefore, lastUpdateAfter))
        {
            return Task.FromResult(CreateResult(
                stateAfter,
                current: null,
                lastUpdateAfter));
        }

        return Task.FromResult(CreateResult(
            stateAfter,
            currentAfter,
            lastUpdateAfter));
    }

    internal static HealthCheckResult CreateResult(
        ApplicationState state,
        ApplicationSnapshot? current,
        ApplicationUpdateResult? lastUpdate)
    {
        var data = CreateData(state, current, lastUpdate);
        if (state is ApplicationState.Stopping or ApplicationState.Stopped)
        {
            return HealthCheckResult.Unhealthy(
                StoppedDescription,
                exception: null,
                data);
        }

        if (current is null ||
            state is not (ApplicationState.Running or ApplicationState.Reloading))
        {
            return HealthCheckResult.Unhealthy(
                UnavailableDescription,
                exception: null,
                data);
        }

        if (lastUpdate?.Status == ApplicationUpdateStatus.Rejected)
        {
            return HealthCheckResult.Degraded(
                DegradedDescription,
                exception: null,
                data);
        }

        return HealthCheckResult.Healthy(HealthyDescription, data);
    }

    private static IReadOnlyDictionary<string, object> CreateData(
        ApplicationState state,
        ApplicationSnapshot? current,
        ApplicationUpdateResult? lastUpdate)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["applicationState"] = state.ToString()
        };

        if (current is not null)
        {
            data["activeRevisionId"] = current.RevisionId;
            data["activeSequence"] = current.Sequence;
        }

        if (lastUpdate is not null)
        {
            data["requestedRevisionId"] = lastUpdate.RequestedRevisionId;
            data["lastUpdateStatus"] = lastUpdate.Status.ToString();
            var diagnostic = lastUpdate.Diagnostics.LastOrDefault();
            if (diagnostic is not null)
            {
                data["diagnosticStage"] = diagnostic.Stage.ToString();
                data["diagnosticCode"] = diagnostic.Error.Code;
            }
        }

        return data;
    }
}
