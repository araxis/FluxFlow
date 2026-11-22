using System.Collections.Concurrent;
using FluxFlow.Environments;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Execution;

public sealed class LocalEnvironmentRunner(WorkflowExecution execution, ILogger<LocalEnvironmentRunner> logger, EnvironmentCheck check,
    IEnumerable<Plugins.IWorkflowHostSupport>? hostSupport = null, Plugins.InstalledPlugins? plugins = null) : BackgroundService, IEnvironmentRunner
{
    public async Task<EnvironmentCheckResult> CheckAsync(EnvironmentRunRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            request.ValidateInstances();
            using var configuration = WorkflowExecution.ReadConfiguration(request.Configuration);
            WorkflowRuntimeCheck.Validate(configuration, cancellationToken, plugins);
            foreach (var support in hostSupport ?? []) await support.CheckAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(false, error.Message);
        }
        return await check.CheckAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private readonly ConcurrentDictionary<Guid, Entry> _runs = new();
    private readonly object _admission = new();
    private bool _stopping;

    internal (byte[] Content, long Dropped) ReadLogs(Guid id, bool diagnostics)
    {
        if (!_runs.TryGetValue(id, out var entry)) return ([], 0);
        entry.LastSeen = DateTimeOffset.UtcNow;
        return (diagnostics ? entry.Observations.Diagnostics : entry.Observations.Logs).Export();
    }

    internal (byte[] Content, long Dropped) ReadTraces(Guid id)
    {
        if (!_runs.TryGetValue(id, out var entry)) return ([], 0);
        entry.LastSeen = DateTimeOffset.UtcNow;
        return entry.Observations.Traces.Export();
    }

    public async Task<EnvironmentRunStatus> StartAsync(Guid id, EnvironmentRunRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Entry entry;
        lock (_admission)
        {
            if (_stopping) throw new InvalidOperationException("The local companion is stopping.");
            entry = new Entry(id);
            if (!_runs.TryAdd(id, entry)) { entry.Cancellation.Dispose(); throw new ArgumentException("This run already exists."); }
        }
        using var cancelled = cancellationToken.Register(entry.Cancellation.Cancel);
        using var scope = logger.BeginScope(entry.Observations.Diagnostics);
        logger.LogInformation("Run requested.");
        try
        {
            ArgumentNullException.ThrowIfNull(request.Environment);
            request.Environment.Validate();
            request.ValidateInstances();
            request = request with { Environment = request.Environment.Copy(), Instances = request.Instances.Select(instance => instance with
                { Bindings = new(instance.Bindings, StringComparer.OrdinalIgnoreCase),
                  ExistingServices = new(instance.ExistingServices, StringComparer.OrdinalIgnoreCase) }).ToArray() };
            var run = await execution.StartAsync(request, entry.Cancellation.Token, entry.Observations).ConfigureAwait(false);
            entry.Started.TrySetResult(run);
        }
        catch (Exception error)
        {
            if (entry.Cancellation.IsCancellationRequested) logger.LogInformation(error, "Startup cancelled.");
            else logger.LogError(error, "Startup failed.");
            entry.Started.TrySetException(error);
        }
        return await GetAsync(id, CancellationToken.None).ConfigureAwait(false);
    }

    public Task<EnvironmentRunStatus> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_runs.TryGetValue(id, out var entry)) return Task.FromResult(new EnvironmentRunStatus(id, "Stopped", []));
        entry.LastSeen = DateTimeOffset.UtcNow;
        if (entry.Stopped) return Task.FromResult(entry.WithObservations(new(id, "Stopped", [])));
        if (entry.Stopping) return Task.FromResult(entry.WithObservations(new(id, "Stopping", [])));
        if (entry.StopError is { } stopError) return Task.FromResult(entry.WithObservations(new(id, "Failed", [], stopError)));
        var task = entry.Started.Task;
        var status = !task.IsCompleted ? new EnvironmentRunStatus(id, "Preparing", [])
            : task.IsFaulted ? new(id, "Failed", [], task.Exception!.ToString())
            : task.Result.Completion?.IsFaulted == true ? new(id, "Failed", task.Result.Services, task.Result.Completion.Exception!.ToString())
            : new(id, "Running", task.Result.Services);
        if (task.IsCompletedSuccessfully && task.Result.Completion is { IsCompleted: true } completion
            && Interlocked.Exchange(ref entry.CompletionReported, 1) == 0)
        {
            using var scope = logger.BeginScope(entry.Observations.Diagnostics);
            if (completion.IsFaulted) logger.LogError(completion.Exception, "Workflow execution failed.");
            else if (completion.IsCanceled) logger.LogInformation("Workflow execution cancelled.");
            else logger.LogInformation("Workflow execution completed.");
        }
        return Task.FromResult(entry.WithObservations(status));
    }

    public async Task StopAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(id, out var entry)) return;
        entry.Cancellation.Cancel();
        await entry.StopGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var scope = logger.BeginScope(entry.Observations.Diagnostics);
        try
        {
            if (entry.Stopped) return;
            // Retain the original execution failure even when Stop precedes status polling.
            await GetAsync(id, CancellationToken.None).ConfigureAwait(false);
            entry.Stopping = true;
            logger.LogInformation("Stopping execution and cleaning up run resources.");
            WorkflowRun? run = null;
            try { run = await entry.Started.Task.ConfigureAwait(false); }
            catch { /* Startup already records the original error and rolls back its resources. */ }
            if (run is not null) await run.DisposeAsync().ConfigureAwait(false);
            if (run is null) logger.LogInformation("Stop request completed.");
            else logger.LogInformation("Execution stopped; run resource cleanup completed.");
            entry.Observations.Complete();
            entry.Stopped = true;
            entry.LastSeen = DateTimeOffset.UtcNow;
        }
        catch (Exception error) { entry.StopError = error.ToString(); logger.LogError(error, "Run resource cleanup failed."); throw; }
        finally { entry.Stopping = false; entry.StopGate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                foreach (var entry in _runs.Values)
                    if (DateTimeOffset.UtcNow - entry.LastSeen > TimeSpan.FromMinutes(2))
                        try
                        {
                            if (entry.Stopped) _runs.TryRemove(entry.Id, out _);
                            else await StopAsync(entry.Id, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception error) { logger.LogError(error, "Environment cleanup failed for run {RunId}.", entry.Id); }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_admission) _stopping = true;
        foreach (var entry in _runs.Values) entry.Cancellation.Cancel();
        List<Exception> failures = [];
        foreach (var id in _runs.Keys)
            try { await StopAsync(id, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (failures.Count != 0) throw new AggregateException("Companion cleanup failed.", failures);
    }

    private sealed class Entry(Guid id)
    {
        public Guid Id { get; } = id;
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<WorkflowRun> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SemaphoreSlim StopGate { get; } = new(1);
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
        public volatile bool Stopping;
        public volatile bool Stopped;
        public ExecutionLogBuffer Logs => Observations.Logs;
        public RunObservations Observations { get; } = new(id);
        public int CompletionReported;
        public EnvironmentRunStatus WithObservations(EnvironmentRunStatus status) => status with
        { Logs = Logs.Snapshot(), Diagnostics = Observations.Diagnostics.Snapshot(), Outputs = Observations.Outputs.Snapshot(), Traces = Observations.Traces.Snapshot() };
        public string? StopError { get; set; }
    }
}
