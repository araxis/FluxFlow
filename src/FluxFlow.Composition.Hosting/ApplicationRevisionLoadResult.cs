using FluxFlow.Data;
using FluxFlow.Engine;

namespace FluxFlow.Composition.Hosting;

[Obsolete("Use ApplicationUpdateResult from FluxFlow.Engine.")]
public sealed class ApplicationRevisionLoadResult
{
    internal ApplicationRevisionLoadResult(ApplicationUpdateResult update)
    {
        Update = update ?? throw new ArgumentNullException(nameof(update));
        Error = update.Diagnostics
            .FirstOrDefault(static diagnostic => diagnostic.Stage == ApplicationUpdateStage.Source)
            ?.Error;
    }

    public ApplicationUpdateResult Update { get; }

    public FlowError? Error { get; }

    public bool Succeeded => !Update.IsRejected;
}
