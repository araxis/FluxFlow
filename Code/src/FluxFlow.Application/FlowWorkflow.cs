using FluxFlow.Application.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Application;

/// <summary>A workflow identity with one DI-owned execution at a time.</summary>
public sealed class FlowWorkflow
{
    private readonly IServiceProvider _host;
    private readonly FlowApplication _application;
    private readonly object _gate = new();
    private WorkflowExecution? _execution;

    internal FlowWorkflow(string name, IServiceProvider host)
    {
        (Name, _host, _application) = (name, host, host.GetRequiredService<FlowApplication>());
        _application.AddWorkflow(this);
    }
    public string Name { get; }

    /// <summary>The current run's native DI scope. Resolve components and ports here,
    /// and reacquire them after restart. Access after Stop is rejected until Start.</summary>
    public IServiceProvider Services => Execution.Services;

    /// <summary>Completion of the current run, including processing and event forwarding.</summary>
    public Task Completion => Execution.Completion;

    internal List<FlowLink> Links { get; } = [];
    internal WorkflowExecution Execution
    {
        get
        {
            lock (_gate)
            {
                if (_execution is not null) return _execution;
                _application.EnsureOpen();
                return _execution = new WorkflowExecution(Name, _host.CreateAsyncScope());
            }
        }
    }

    internal void Renew()
    {
        lock (_gate)
        {
            _application.EnsureOpen();
            if (_execution is not { CanRestart: true })
                throw new InvalidOperationException($"Workflow '{Name}' must stop successfully before restart.");
            _execution = new WorkflowExecution(Name, _host.CreateAsyncScope());
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _application.StartWorkflowAsync(this, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default)
        => _application.StopWorkflowAsync(this, cancellationToken);

    internal WorkflowExecution? ExistingExecution => _execution;
}
