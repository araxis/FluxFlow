using FluxFlow.Nodes;

namespace FluxFlow.Composition;

/// <summary>Maps existing bound ports and lifetime into Core without adding delivery blocks.</summary>
internal class ComponentGraphView : IFlowComponent
{
    private readonly ComponentInstance _instance;

    private ComponentGraphView(ComponentInstance instance)
    {
        _instance = instance;
        var nativePorts = (instance.Node as IFlowComponent)?.Ports;
        Ports = new FlowPorts(
            inputs: instance.Inputs.Values.Where(port => !IsControl(port))
                .ToDictionary(port => port.Name, port => port.Target, StringComparer.Ordinal),
            outputs: instance.Outputs.Values.ToDictionary(port => port.Name, port => port.Source, StringComparer.Ordinal),
            events: instance.Activity,
            controls: instance.Inputs.Values.Where(IsControl)
                .ToDictionary(port => port.Name, port => port.Target, StringComparer.Ordinal));

        bool IsControl(ComponentInputPort port) => nativePorts?.IsControlInput(port.Target) == true;
    }

    internal static IFlowComponent Create(ComponentInstance instance)
        => instance.Node is IFlowSource source ? new SourceView(instance, source) : new ComponentGraphView(instance);

    public FlowPorts Ports { get; }
    public Task Completion => _instance.Completion;
    public void Complete() => _instance.Node.Complete();
    public void Fault(Exception exception) => _instance.Node.Fault(exception);
    public ValueTask DisposeAsync() => _instance.DisposeAsync();

    private sealed class SourceView(ComponentInstance instance, IFlowSource source)
        : ComponentGraphView(instance), IFlowSource
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => source.StartAsync(cancellationToken);
        public void StopProduction() => source.StopProduction();
        public Task ProductionCompletion => source.ProductionCompletion;
    }
}
