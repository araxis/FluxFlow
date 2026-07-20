using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Data;

namespace FluxFlow.Composition.Hosting;

public sealed class ApplicationRevisionLoadResult
{
    private ApplicationRevisionLoadResult(
        ApplicationRevisionUpdateResult? update,
        FlowError? error)
    {
        Update = update;
        Error = error;
    }

    public ApplicationRevisionUpdateResult? Update { get; }

    public FlowError? Error { get; }

    public bool Succeeded => Error is null && Update is not null &&
        Update.Status is not ApplicationRevisionUpdateStatus.Rejected;

    internal static ApplicationRevisionLoadResult FromUpdate(
        ApplicationRevisionUpdateResult update)
        => new(update ?? throw new ArgumentNullException(nameof(update)), error: null);

    internal static ApplicationRevisionLoadResult FromError(FlowError error)
        => new(update: null, error ?? throw new ArgumentNullException(nameof(error)));
}
