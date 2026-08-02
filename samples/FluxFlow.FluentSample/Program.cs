using System.Threading.Tasks.Dataflow;
using FluxFlow.Fluent;
using FluxFlow.Nodes;

// The same three-node pipeline the CompositionSample builds with a registry and a
// string-typed definition, expressed with the fluent DSL. Wiring is checked by the
// compiler: swap in a node whose input type does not match and it will not build.
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

internal sealed class WordSource(IReadOnlyList<string> words) : FlowSource<string>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(word), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class CountSource(int count) : FlowSource<int>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var value = 0; value < count; value++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(value), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class UppercaseNode : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        await EmitAsync(message.With(message.Value.ToUpperInvariant()), Stopping)
            .ConfigureAwait(false);
    }
}

internal sealed class LabelNode(string label) : FlowNode<int, string>
{
    protected override async Task ProcessAsync(FlowMessage<int> message)
    {
        await EmitAsync(message.With($"{label}: {message.Value}"), Stopping)
            .ConfigureAwait(false);
    }
}

internal sealed class EvenOddRouter : FlowNode<int, int>
{
    private readonly FlowOutput<FlowMessage<int>> _even;
    private readonly FlowOutput<FlowMessage<int>> _odd;

    public EvenOddRouter()
    {
        _even = AddOutput<FlowMessage<int>>();
        _odd = AddOutput<FlowMessage<int>>();
    }

    public ISourceBlock<FlowMessage<int>> Even => _even;

    public ISourceBlock<FlowMessage<int>> Odd => _odd;

    protected override async Task ProcessAsync(FlowMessage<int> message)
    {
        var port = message.Value % 2 == 0 ? _even : _odd;
        await EmitAsync(port, message, Stopping).ConfigureAwait(false);
    }
}

internal sealed class CollectSink(StringCollector collector) : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Value);
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
