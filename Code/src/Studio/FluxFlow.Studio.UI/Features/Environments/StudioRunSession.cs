using FluxFlow.Environments;
using FluxFlow.Application.Addressing;
using System.Text.Json;
using FluxFlow.Studio.UI.Features.Designer;

namespace FluxFlow.Studio.UI.Features.Environments;

public sealed partial class StudioRunSession(IEnvironmentRunner runner) : IAsyncDisposable
{
    public EnvironmentRunStatus? Status { get; private set; }
    public string? RunEnvironmentName { get; private set; }
    public bool Busy { get; private set; }
    public bool HasRun => Status is { State: not "Stopped" };
    public string? Failure { get; private set; }
    public string? CleanupError { get; private set; }
    public ExecutionLogSnapshot Logs { get; private set; } = new([], 0);
    public ExecutionLogSnapshot Diagnostics { get; private set; } = new([], 0);
    public ExecutionOutputSnapshot Outputs { get; private set; } = new([], 0);
    public ExecutionTraceSnapshot Traces { get; private set; } = new([], 0);
    public string? LogReadError { get; private set; }
    public bool CanRetry => !Busy && Status is { State: "Failed" or "Stopped" } && (Failure is not null || CleanupError is not null);
    public event Action? Changed;
    private CancellationTokenSource? _startup;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _poll;
    private CancellationTokenSource? _check;
    public bool Checking => _check is not null;
    public EnvironmentCheckResult? CheckResult { get; private set; }
    public long? CheckRevision { get; private set; }
    private DesignerGraphState? _checkGraph;

    internal string? ResolveCheckNode(DesignerGraphState graph, EnvironmentCheckResult result)
    {
        if (result.Success || !ReferenceEquals(_checkGraph, graph) || !ReferenceEquals(CheckResult, result) || CheckRevision != graph.Revision) return null;
        var nodes = graph.GetEnvironmentDefinition().Topology.Nodes;
        return nodes.FirstOrDefault(node => result.NodeId is { } id ? node.Id == id :
            result.Resource is { } resource && node.Kind == EnvironmentNodeKind.Service &&
            string.Equals(node.Resource, resource, StringComparison.OrdinalIgnoreCase))?.Id;
    }
    private DesignerGraphState? _runGraph;
    private long _runRevision;
    private string? _runConfiguration;

    internal IReadOnlyList<PreparedService> CurrentServices(DesignerGraphState graph) =>
        CurrentTopologyStatus(graph) is { State: "Running" } status ? status.Services : [];

    public ComponentAddress? ResolveTraceComponent(DesignerGraphState graph, ExecutionTrace span)
    {
        return IsCurrentGraph(graph) && Traces.Records.Contains(span) ? TraceAddress(span) : null;
    }

    internal IReadOnlyList<ComponentAddress>? RecentTraceComponents(DesignerGraphState graph, long afterSequence)
    {
        if (!IsCurrentGraph(graph)) return null;
        return Traces.Records.Where(span => span.Sequence > afterSequence)
            .Select(TraceAddress).OfType<ComponentAddress>().Distinct().ToArray();
    }

    private bool IsCurrentGraph(DesignerGraphState graph) => ReferenceEquals(_runGraph, graph)
        && graph.IsProjectOpen && graph.Revision == _runRevision && graph.ExportConfiguration() == _runConfiguration;

    private static ComponentAddress? TraceAddress(ExecutionTrace span)
    {
        if (!span.Attributes.TryGetValue("flow.component.address", out var address) || address.ValueKind != JsonValueKind.String) return null;
        try { return ComponentAddress.Parse(address.GetString()!); }
        catch (FormatException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public async Task CheckAsync(DesignerGraphState graph)
    {
        if (Checking) return;
        var revision = graph.Revision;
        _checkGraph = graph;
        CheckRevision = revision;
        CheckResult = null;
        if (graph.CheckTopology() is { } limitation)
        {
            CheckResult = limitation;
            Changed?.Invoke();
            return;
        }
        var request = graph.CreateEnvironmentRunRequest();
        using var check = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        check.CancelAfter(TimeSpan.FromSeconds(20));
        _check = check;
        Changed?.Invoke();
        try
        {
            var result = await runner.CheckAsync(request, check.Token).WaitAsync(check.Token);
            if (!check.IsCancellationRequested && graph.Revision == revision && CheckRevision == revision) CheckResult = result;
        }
        catch (OperationCanceledException) when (check.IsCancellationRequested)
        {
            if (!_lifetime.IsCancellationRequested && CheckRevision == revision && graph.Revision == revision)
                CheckResult = new(false, "The environment check timed out. Check the companion connection and try again.");
        }
        catch (Exception error)
        {
            if (!check.IsCancellationRequested && graph.Revision == revision && CheckRevision == revision)
                CheckResult = new(false, $"Environment check failed: {error.Message}");
        }
        finally { _check = null; Changed?.Invoke(); }
    }

    public void CancelCheck()
    {
        CheckRevision = null;
        CheckResult = null;
        _check?.Cancel();
        Changed?.Invoke();
    }

    public async Task StartAsync(DesignerGraphState graph)
    {
        if (Busy || HasRun) return;
        if (graph.EnvironmentLimitation() is { } limitation) throw new InvalidOperationException(limitation);
        var request = graph.CreateEnvironmentRunRequest();
        var id = Guid.NewGuid();
        _runGraph = graph;
        _runRevision = graph.Revision;
        _runConfiguration = request.Configuration;
        CaptureTopology(graph);
        RunEnvironmentName = graph.EnvironmentName;
        Failure = CleanupError = null;
        Logs = new([], 0);
        Diagnostics = new([], 0);
        Outputs = new([], 0);
        Traces = new([], 0);
        LogReadError = null;
        Status = new(id, "Preparing", []);
        Busy = true;
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _startup = startup;
        Changed?.Invoke();
        _poll = PollAsync(id);
        try
        {
            var status = await runner.StartAsync(id, request, startup.Token);
            if (!startup.IsCancellationRequested && Status?.Id == id && status.Id == id) UpdateStatus(status);
        }
        catch (OperationCanceledException) when (startup.IsCancellationRequested) { }
        catch (Exception error) { if (!startup.IsCancellationRequested && Status?.Id == id) UpdateStatus(new(id, "Failed", [], error.ToString())); }
        finally
        {
            if (ReferenceEquals(_startup, startup)) { _startup = null; Busy = false; Changed?.Invoke(); }
        }
    }

    public async Task StopAsync()
    {
        if (Status is not { State: not "Stopped" and not "Stopping" } current) return;
        _startup?.Cancel();
        _startup = null;
        Busy = true;
        CleanupError = null;
        Status = current with { State = "Stopping" };
        Changed?.Invoke();
        try
        {
            await runner.StopAsync(current.Id, CancellationToken.None);
            Status = new(current.Id, "Stopped", []);
            // Read once after cleanup to include shutdown events, then retain them locally.
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var final = await runner.GetAsync(current.Id, timeout.Token).WaitAsync(timeout.Token);
                if (Status?.Id == current.Id) UpdateLogs(final);
            }
            catch (Exception error) { LogReadError = $"Could not read final execution logs: {error.Message}"; }
        }
        catch (Exception error) { CleanupError = error.ToString(); Status = current with { State = "Failed", Error = CleanupError }; }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public async Task RetryAsync(DesignerGraphState graph)
    {
        if (!CanRetry) return;
        if (HasRun) await StopAsync();
        if (!HasRun) await StartAsync(graph);
    }

    private void UpdateStatus(EnvironmentRunStatus status)
    {
        Status = status;
        Failure ??= status.Error;
        UpdateLogs(status);
    }

    private void UpdateLogs(EnvironmentRunStatus status)
    {
        if (status.Id != Status?.Id) return;
        if (status.Logs is { } logs && (logs.Records.LastOrDefault()?.Sequence ?? 0) >= (Logs.Records.LastOrDefault()?.Sequence ?? 0))
            Logs = logs;
        if (status.Diagnostics is { } diagnostics && (diagnostics.Records.LastOrDefault()?.Sequence ?? 0) >= (Diagnostics.Records.LastOrDefault()?.Sequence ?? 0))
            Diagnostics = diagnostics;
        if (status.Outputs is { } outputs)
        {
            var sequence = outputs.Records.LastOrDefault()?.Sequence ?? 0;
            var current = Outputs.Records.LastOrDefault()?.Sequence ?? 0;
            if (sequence > current || (sequence == current && outputs.Dropped >= Outputs.Dropped)) Outputs = outputs;
        }
        if (status.Traces is { } traces && (traces.Records.LastOrDefault()?.Sequence ?? 0) >= (Traces.Records.LastOrDefault()?.Sequence ?? 0))
            Traces = traces;
        LogReadError = null;
    }

    private async Task PollAsync(Guid id)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token) && Status?.Id == id && HasRun)
            {
                var observed = Status;
                EnvironmentRunStatus status;
                try { status = await runner.GetAsync(id, _lifetime.Token); }
                // Start/Stop owns status while busy; a failed poll must not end live log updates.
                catch (Exception) when (Busy && Status?.Id == id && !_lifetime.IsCancellationRequested) { continue; }
                if (Status?.Id != id || status.Id != id || !HasRun || _lifetime.IsCancellationRequested) continue;
                if (!Busy && ReferenceEquals(Status, observed) && CleanupError is null) UpdateStatus(status);
                else UpdateLogs(status);
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!Busy && HasRun && Status?.Id == id && CleanupError is null) { UpdateStatus(Status! with { State = "Failed", Error = error.ToString() }); Changed?.Invoke(); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        CancelCheck();
        if (HasRun) await StopAsync();
        if (_poll is not null) await _poll;
        _lifetime.Dispose();
    }
}

internal sealed class UnavailableEnvironmentRunner : IEnvironmentRunner
{
    public Task<EnvironmentCheckResult> CheckAsync(EnvironmentRunRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new EnvironmentCheckResult(false, "This host has not configured a local companion."));
    public Task<EnvironmentRunStatus> StartAsync(Guid id, EnvironmentRunRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This host has not configured a local companion.");
    public Task<EnvironmentRunStatus> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(new EnvironmentRunStatus(id, "Stopped", []));
    public Task StopAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
}
