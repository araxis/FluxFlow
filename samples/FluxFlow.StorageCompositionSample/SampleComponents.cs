using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.StorageCompositionSample;

internal static class SampleNodeTypes
{
    public static readonly NodeType PutSource = new("sample.storage-put-source");
    public static readonly NodeType GetSource = new("sample.storage-get-source");
    public static readonly NodeType DeleteSource = new("sample.storage-delete-source");
    public static readonly NodeType ResultSink = new("sample.storage-result-sink");
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
            new FlowNodeRegistration(SampleNodeTypes.PutSource, PutSourceNode.Create),
            new FlowNodeRegistration(SampleNodeTypes.GetSource, GetSourceNode.Create),
            new FlowNodeRegistration(SampleNodeTypes.DeleteSource, DeleteSourceNode.Create),
            new FlowNodeRegistration(
                SampleNodeTypes.ResultSink,
                context => ResultSinkNode.Create(context, capture))
        ];
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}

internal sealed class PutSourceNode(IReadOnlyList<SamplePutRecord> records)
    : SourceFlowNode<StoragePutRequest>(new DataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var node = new PutSourceNode(SampleNodeOptions.ReadRequired<List<SamplePutRecord>>(
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
            await SendOutputAsync(new StoragePutRequest
            {
                Key = record.Key,
                Value = record.Value,
                ContentType = "text/plain",
                CorrelationId = $"put-{record.Key}"
            }, cancellationToken).ConfigureAwait(false);
        }

        CompleteOutput();
    }
}

internal sealed class GetSourceNode(IReadOnlyList<string> keys)
    : SourceFlowNode<StorageGetRequest>(new DataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var node = new GetSourceNode(SampleNodeOptions.ReadRequired<List<string>>(
            context.Definition,
            "keys"));
        return context.CreateNode(node)
            .Output("Output", node.Output)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            await SendOutputAsync(new StorageGetRequest
            {
                Key = key,
                CorrelationId = $"get-{key}"
            }, cancellationToken).ConfigureAwait(false);
        }

        CompleteOutput();
    }
}

internal sealed class DeleteSourceNode(IReadOnlyList<string> keys)
    : SourceFlowNode<StorageDeleteRequest>(new DataflowBlockOptions { BoundedCapacity = 8 })
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var node = new DeleteSourceNode(SampleNodeOptions.ReadRequired<List<string>>(
            context.Definition,
            "keys"));
        return context.CreateNode(node)
            .Output("Output", node.Output)
            .Build();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            await SendOutputAsync(new StorageDeleteRequest
            {
                Key = key,
                CorrelationId = $"delete-{key}"
            }, cancellationToken).ConfigureAwait(false);
        }

        CompleteOutput();
    }
}

internal sealed class ResultSinkNode : SinkFlowNode<StorageResult>
{
    private readonly string _stage;
    private readonly SampleCapture _capture;

    private ResultSinkNode(string stage, SampleCapture capture)
        : base(new ExecutionDataflowBlockOptions { BoundedCapacity = 8 })
    {
        _stage = stage;
        _capture = capture;
    }

    public static RuntimeNode Create(RuntimeNodeFactoryContext context, SampleCapture capture)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(capture);

        var node = new ResultSinkNode(
            SampleNodeOptions.ReadOptional(context.Definition, "stage", "default"),
            capture);
        return context.CreateNode(node)
            .Input("Input", node.Input)
            .Build();
    }

    protected override ValueTask HandleAsync(
        StorageResult input,
        CancellationToken cancellationToken)
    {
        _capture.Add(_stage, input);
        return ValueTask.CompletedTask;
    }
}

internal static class SampleNodeOptions
{
    public static T ReadRequired<T>(NodeDefinition definition, string name)
    {
        if (!definition.Configuration.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Missing required option '{name}'.");
        }

        return value.Deserialize<T>() ?? throw new InvalidOperationException($"Option '{name}' was empty.");
    }

    public static string ReadOptional(NodeDefinition definition, string name, string fallback)
    {
        if (!definition.Configuration.TryGetValue(name, out var value))
        {
            return fallback;
        }

        return value.GetString() ?? fallback;
    }
}
