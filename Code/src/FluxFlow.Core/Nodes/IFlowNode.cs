using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Core.Nodes;

/// <summary>Lifecycle shared by every node: complete it, fault it, await it, dispose it.</summary>
public interface IFlowNode : IAsyncDisposable
{
    /// <summary>Inputs kept open until accepted business output has drained.</summary>
    IEnumerable<ITargetBlock<FlowMessage>> TerminalInputs => [];

    Task Completion { get; }

    void Complete();

    void Fault(Exception exception);
}

/// <summary>A node that must be started to begin producing (timers, watchers, triggers).</summary>
public interface IFlowSource : IFlowNode
{
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops ingress without closing terminal control inputs.</summary>
    void StopProduction() => Complete();

    /// <summary>Production and data-output drain; sources without controls have one phase.</summary>
    Task ProductionCompletion => Completion;
}
