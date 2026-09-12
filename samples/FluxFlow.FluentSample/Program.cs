using FluxFlow.Data;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Fluent;
using FluxFlow.Nodes;

// Directly constructed components share one canonical Core graph.
// Each component materializes only the value it needs; no host or descriptors are created.
var upperCollector = new StringCollector();

await using (var linear = Flow
    .From(new WordSource(["alpha", "beta", "gamma"]))
    .Then(new UppercaseNode())
    .To(new CollectSink(upperCollector))
    .Build())
{
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

await using (var branched = Flow
    .From(new CountSource(6)) // 0..5
    .Then(router)
    .Branch(router.Even, even => even.Then(new LabelNode("even")).To(sink))
    .Branch(router.Odd, odd => odd.Then(new LabelNode("odd")).To(sink))
    .Build())
{
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
            await EmitAsync(FlowMessage.Create(FlowValue.From(word)), cancellationToken)
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
            await EmitAsync(FlowMessage.Create(FlowValue.From(value)), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class UppercaseNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        await EmitAsync(message.With(FlowValue.From(message.Value!.Deserialize<string>()!.ToUpperInvariant())), Stopping)
            .ConfigureAwait(false);
    }
}

internal sealed class LabelNode(string label) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        await EmitAsync(message.With(FlowValue.From($"{label}: {message.Value!.Deserialize<int>()}")), Stopping)
            .ConfigureAwait(false);
    }
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
                ["Output"] = Output, ["Even"] = Even, ["Odd"] = Odd
            }, Events);
    }

    public override FlowPorts Ports { get; }

    public ISourceBlock<FlowMessage> Even => _even;

    public ISourceBlock<FlowMessage> Odd => _odd;

    protected override async Task ProcessAsync(FlowMessage message)
    {
        var port = message.Value!.Deserialize<int>() % 2 == 0 ? _even : _odd;
        await EmitAsync(port, message, Stopping).ConfigureAwait(false);
    }
}

internal sealed class CollectSink(StringCollector collector) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        collector.Add(message.Value!.Deserialize<string>()!);
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
