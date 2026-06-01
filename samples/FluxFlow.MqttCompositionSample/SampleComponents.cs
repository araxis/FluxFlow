using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.MqttCompositionSample;

internal static class SampleNodeTypes
{
    public static readonly NodeType PublishResultSink = new("sample.mqtt-publish-result-sink");
}

internal sealed class SampleStore
{
    private readonly object _gate = new();
    private readonly List<MqttPublishResult> _results = [];

    public void Add(MqttPublishResult result)
    {
        lock (_gate)
        {
            _results.Add(result);
        }
    }

    public IReadOnlyList<MqttPublishResult> GetResults()
    {
        lock (_gate)
        {
            return _results.ToArray();
        }
    }
}

internal static class SampleComponentRegistration
{
    public static RuntimeNodeFactoryRegistry RegisterSampleComponents(
        this RuntimeNodeFactoryRegistry registry,
        SampleStore store)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(store);

        return registry.Register(
            SampleNodeTypes.PublishResultSink,
            context => PublishResultSinkNode.Create(context, store));
    }
}

internal sealed class PublishResultSinkNode(SampleStore store) : SinkFlowNode<MqttPublishResult>(
    new ExecutionDataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context, SampleStore store)
    {
        var node = new PublishResultSinkNode(store);
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Build();
    }

    protected override ValueTask HandleAsync(
        MqttPublishResult input,
        CancellationToken cancellationToken)
    {
        store.Add(input);
        return ValueTask.CompletedTask;
    }
}
