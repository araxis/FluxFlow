using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.SessionsCompositionSample;

internal static class SampleNodeTypes
{
    public static readonly NodeType Source = new("sample.session-input");
    public static readonly NodeType Sink = new("sample.session-sink");
}

internal static class SampleComponentRegistration
{
    public static RuntimeNodeFactoryRegistry RegisterSampleComponents(
        this RuntimeNodeFactoryRegistry registry,
        SampleCapture capture)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(capture);

        return registry.Register(new SampleComponentModule(capture));
    }
}

internal sealed class SampleComponentModule : IFlowNodeModule
{
    public SampleComponentModule(SampleCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        Registrations =
        [
            new FlowNodeRegistration(SampleNodeTypes.Source, SampleSourceNode.Create),
            new FlowNodeRegistration(
                SampleNodeTypes.Sink,
                context => SampleSinkNode.Create(context, capture))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}

internal sealed class SampleSourceNode(IReadOnlyList<SessionRecordInput> records)
    : SourceFlowNode<SessionRecordInput>(new DataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var node = new SampleSourceNode(ReadRequired<List<SessionRecordInput>>(
            context.Definition,
            "records"));
        return context.CreateNode(node)
            .Output("Output", node.Output)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var record in records)
        {
            await SendOutputAsync(record, cancellationToken).ConfigureAwait(false);
        }

        CompleteOutput();
    }

    private static T ReadRequired<T>(NodeDefinition definition, string name)
    {
        if (!definition.Configuration.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Missing required option '{name}'.");
        }

        return value.Deserialize<T>() ?? throw new InvalidOperationException($"Option '{name}' was empty.");
    }
}

internal sealed class SampleSinkNode : SinkFlowNode<SessionRecord>
{
    private readonly string _stage;
    private readonly SampleCapture _capture;

    private SampleSinkNode(string stage, SampleCapture capture)
        : base(new ExecutionDataflowBlockOptions { BoundedCapacity = 8 })
    {
        _stage = stage;
        _capture = capture;
    }

    public static RuntimeNode Create(RuntimeNodeFactoryContext context, SampleCapture capture)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(capture);

        var node = new SampleSinkNode(
            ReadOptional(context.Definition, "stage", "default"),
            capture);
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Build();
    }

    protected override ValueTask HandleAsync(
        SessionRecord input,
        CancellationToken cancellationToken)
    {
        _capture.Add(_stage, input);
        return ValueTask.CompletedTask;
    }

    private static string ReadOptional(NodeDefinition definition, string name, string fallback)
    {
        if (!definition.Configuration.TryGetValue(name, out var value))
        {
            return fallback;
        }

        return value.GetString() ?? fallback;
    }
}
