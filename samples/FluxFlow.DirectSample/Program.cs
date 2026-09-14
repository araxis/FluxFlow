using System.Text.Json;
using FluxFlow.Data;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Nodes;

// Directly constructed components share one canonical Core graph.
// Each component materializes only the value it needs; no host or descriptors are created.
var upperCollector = new StringCollector();

await using (var linear = new FlowApplication())
{
    linear.Workflow("main", workflow =>
    {
        var source = workflow.Add("words", new WordSource(["alpha", "beta", "gamma"]));
        var upper = workflow.Add("uppercase", new UppercaseNode());
        var target = workflow.Add("collector", new CollectSink(upperCollector));
        source.Output.To(upper.Input, when: message => !string.IsNullOrWhiteSpace(message.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        upper.Output.To(target.Input);
        target.Output.DiscardUnmatched();
    });
    await linear.StartAsync();
    await linear.Completion.WaitAsync(TimeSpan.FromSeconds(5));
}

Console.WriteLine("Linear pipeline (upper-cased):");
foreach (var item in upperCollector.Items)
{
    Console.WriteLine($"  {item}");
}

// Branching: route by parity into two sub-pipelines, then fan both back into one sink
// by passing the same sink instance to each branch.
var labelled = new StringCollector();
var sink = new CollectSink(labelled);
var router = new EvenOddRouter();

await using (var branched = new FlowApplication())
{
    var workflow = branched.AddWorkflow("main");
    var source = workflow.Add("numbers", new CountSource(6));
    var route = workflow.Add("parity", router);
    var even = workflow.Add("even", new LabelNode("even"));
    var odd = workflow.Add("odd", new LabelNode("odd"));
    var target = workflow.Add("collector", sink);
    source.Output.LinkTo(route.Input);
    route.Outputs["Even"].LinkTo(even.Input);
    route.Outputs["Odd"].LinkTo(odd.Input);
    even.Output.LinkTo(target.Input);
    odd.Output.LinkTo(target.Input);
    target.Output.DiscardUnmatched();
    await branched.StartAsync();
    await branched.Completion.WaitAsync(TimeSpan.FromSeconds(5));
}

Console.WriteLine();
Console.WriteLine("Branched pipeline (routed by parity, fanned into one sink):");
foreach (var item in labelled.Items.OrderBy(item => item, StringComparer.Ordinal))
{
    Console.WriteLine($"  {item}");
}

return 0;

internal sealed class WordSource(IReadOnlyList<string> words) : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(word, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class CountSource(int count) : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var value = 0; value < count; value++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class UppercaseNode : FlowValueTransformNode
{
    protected override ValueTask<FlowValue> ProcessAsync(FlowValue input, CancellationToken cancellationToken)
        => ValueTask.FromResult((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(input.Json.Deserialize<string>()!.ToUpperInvariant(), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
}

internal sealed class LabelNode(string label) : FlowValueTransformNode
{
    protected override ValueTask<FlowValue> ProcessAsync(FlowValue input, CancellationToken cancellationToken)
        => ValueTask.FromResult((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement($"{label}: {input.Json.Deserialize<int>()}", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
}

internal sealed class EvenOddRouter : FlowNode
{
    private readonly FlowOutput<FlowMessage> _even;
    private readonly FlowOutput<FlowMessage> _odd;

    public EvenOddRouter()
    {
        _even = AddOutput<FlowMessage>();
        _odd = AddOutput<FlowMessage>();
        Ports = new FlowPorts(
            new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = Input },
            new Dictionary<string, ISourceBlock<FlowMessage>>
            {
                ["Output"] = Output,
                ["Even"] = Even,
                ["Odd"] = Odd
            }, Events);
    }

    public override FlowPorts Ports { get; }

    public ISourceBlock<FlowMessage> Even => _even;

    public ISourceBlock<FlowMessage> Odd => _odd;

    protected override async Task ProcessAsync(FlowMessage message)
    {
        var port = message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) % 2 == 0 ? _even : _odd;
        await EmitAsync(port, message, Stopping).ConfigureAwait(false);
    }
}

internal sealed class CollectSink(StringCollector collector) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        collector.Add(message.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}

internal sealed class StringCollector
{
    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_items)
            {
                return _items.ToArray();
            }
        }
    }

    public void Add(string item)
    {
        lock (_items)
        {
            _items.Add(item);
        }
    }
}
