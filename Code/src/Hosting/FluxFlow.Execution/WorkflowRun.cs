using FluxFlow.Environments;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Execution;

public sealed class WorkflowRun : IAsyncDisposable
{
    private readonly IReadOnlyList<WorkflowInstanceRun> _instances;
    private readonly PreparedEnvironment _environment;
    private readonly RunObservations? _observations;
    private readonly ExecutionTraceCollector? _traces;
    private readonly ILogger _logger;
    internal WorkflowRun(IReadOnlyList<WorkflowInstanceRun> instances, PreparedEnvironment environment,
        RunObservations? observations, ExecutionTraceCollector? traces, ILogger logger)
        => (_instances, _environment, _observations, _traces, _logger) = (instances, environment, observations, traces, logger);
    public IReadOnlyList<PreparedService> Services => _environment.Services;
    public Task? Completion { get; private set; }
    internal void ObserveCompletion() => Completion = ObserveAsync(_instances.Select(instance => instance.Completion).ToList());
    private static async Task ObserveAsync(List<Task> pending)
    {
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
            pending.Remove(completed);
        }
    }
    public async ValueTask DisposeAsync()
    {
        using var traceScope = _observations is null ? null : _traces?.Capture(_observations.Traces);
        using var scope = _observations is null ? null : _logger.BeginScope(_observations.Logs);
        List<Exception> failures = [];
        foreach (var instance in _instances.Reverse())
            try { await instance.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        try { await _environment.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException("Workflow shutdown failed.", failures);
    }
}
