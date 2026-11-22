using FluxFlow.Application;
using FluxFlow.Environments;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Execution;

internal sealed class WorkflowInstanceRun(IHost host, FlowApplication application, ApplicationInstance identity, RunObservations? observations,
    ExecutionTraceCollector? traces, ILogger logger) : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
    internal WorkflowOutputCapture? OutputCapture { get; set; }
    private readonly object _disposeGate = new();
    private Task? _dispose;
    public Task Completion => application.Completion;

    public ValueTask DisposeAsync()
    {
        // A failed native disposal may be a no-op on retry. Retain its result so a
        // later Stop cannot incorrectly claim that resource cleanup succeeded.
        lock (_disposeGate) return new(_dispose ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        using var instanceScope = logger.BeginScope(WorkflowExecution.InstanceAttributes(identity));
        using var traceScope = observations is null ? null : traces?.Capture(observations.Traces, identity);
        using var scope = observations is null ? null : logger.BeginScope(observations.Logs);
        List<Exception> failures = [];
        Task? completion = null;
        try { completion = application.Completion; }
        catch (InvalidOperationException) { /* Startup may not have reached execution. */ }
        var executionErrors = completion?.Exception?.Flatten().InnerExceptions.ToArray() ?? [];
        var failureMessage = "Workflow shutdown failed.";
        using var deadline = new CancellationTokenSource(StopTimeout);
        try { await host.StopAsync(deadline.Token).ConfigureAwait(false); }
        catch (Exception error)
        {
            if (deadline.IsCancellationRequested)
                failureMessage = $"Workflow shutdown failed. The {StopTimeout.TotalSeconds}-second host Stop deadline elapsed.";
            AddCleanupFailures(error, executionErrors, failures);
        }
        try { await application.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { AddCleanupFailures(error, executionErrors, failures); }
        try
        {
            if (host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync().ConfigureAwait(false);
            else host.Dispose();
        }
        catch (Exception error) { failures.Add(error); }
        try { if (OutputCapture is not null) await OutputCapture.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException(failureMessage, failures);
    }

    internal static void AddCleanupFailures(Exception error, IReadOnlyCollection<Exception> executionErrors, List<Exception> failures)
    {
        IEnumerable<Exception> errors = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [error];
        foreach (var failure in errors)
            if (!executionErrors.Any(original => ReferenceEquals(original, failure))) failures.Add(failure);
    }
}
