using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Engine;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

var collector = new StringCollector();
var definitionBuilder = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var main);

main
    .AddComponent(
        "source",
        SampleComponents.Source,
        options => options.Messages = ["alpha", "beta"],
        out var source)
    .AddComponent(
        "upper",
        SampleComponents.Uppercase,
        out var upper)
    .AddComponent(
        "sink",
        SampleComponents.Sink,
        out var sink);

source.Output.ConnectTo(upper.Input);
upper.Output.ConnectTo(sink.Input);

var definition = definitionBuilder.Build();

var services = new ServiceCollection();
services.AddSingleton(collector);
services.AddFluxFlow(definition, options => options.StartWithHost = false);

await using var provider = services.BuildServiceProvider();
var application = provider.GetRequiredService<FluxFlowApplication>();
var result = await application.StartAsync();
if (result.IsRejected)
{
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine(
            $"{diagnostic.Error.Message} {JsonSerializer.Serialize(diagnostic.Error.Details)}");
    }

    return 1;
}

await WaitForItemsAsync(collector, expectedCount: 2, TimeSpan.FromSeconds(5));
await application.StopAsync();

foreach (var item in collector.Items)
{
    Console.WriteLine(item);
}

return 0;

static async Task WaitForItemsAsync(
    StringCollector collector,
    int expectedCount,
    TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    while (collector.Items.Count < expectedCount)
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
}

internal sealed record SourceOptions
{
    public string[] Messages { get; init; } = [];
}

internal static class SampleComponentTypes
{
    public const string Source = "sample.source";
    public const string Uppercase = "sample.uppercase";
    public const string Sink = "sample.sink";
}

internal static class SampleComponentPorts
{
    public const string Input = "Input";
    public const string Output = "Output";
    public const string Events = "Events";
}

internal static class SampleComponentOptions
{
    public const string Messages = "messages";
}

internal static class SampleComponents
{
    public static ComponentContract<SourceComponentBuilder, SourceComponentHandle> Source { get; } =
        ComponentContract.Create(
            SampleComponentTypes.Source,
            static runtime =>
            {
                runtime
                    .UseFactory(static context =>
                    {
                        var options = context.BindConfiguration<SourceOptions>();
                        return new StringSourceNode(options.Messages);
                    })
                    .HasOutput(SampleComponentPorts.Output, static node => node.Output)
                    .HasEvents(SampleComponentPorts.Events, static node => node.Events);
            },
            static () => new SourceComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new SourceComponentHandle(component));

    public static ComponentContract<UppercaseComponentHandle> Uppercase { get; } =
        ComponentContract.Create(
            SampleComponentTypes.Uppercase,
            static runtime =>
            {
                runtime
                    .UseFactory(static _ => new UppercaseNode())
                    .HasInput(SampleComponentPorts.Input, static node => node.Input)
                    .HasOutput(SampleComponentPorts.Output, static node => node.Output)
                    .HasEvents(SampleComponentPorts.Events, static node => node.Events);
            },
            static component => new UppercaseComponentHandle(component));

    public static ComponentContract<SinkComponentHandle> Sink { get; } =
        ComponentContract.Create(
            SampleComponentTypes.Sink,
            static runtime =>
            {
                runtime
                    .UseFactory(static context => new CollectSinkNode(
                        context.Services.GetRequiredService<StringCollector>()))
                    .HasInput(SampleComponentPorts.Input, static node => node.Input)
                    .HasEvents(SampleComponentPorts.Events, static node => node.Events);
            },
            static component => new SinkComponentHandle(component));
}

internal sealed class SourceComponentBuilder
{
    public string[] Messages { get; set; } = [];

    internal void Apply(ComponentDefinitionBuilder definition)
        => definition.Set(SampleComponentOptions.Messages, Messages);
}

internal sealed class SourceComponentHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public OutputPortHandle<string> Output { get; } = definition.Output<string>(SampleComponentPorts.Output);
    public OutputPortHandle<ComponentEvent> Events { get; } = definition.Output<ComponentEvent>(SampleComponentPorts.Events);
}

internal sealed class UppercaseComponentHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<string> Input { get; } = definition.Input<string>(SampleComponentPorts.Input);
    public OutputPortHandle<string> Output { get; } = definition.Output<string>(SampleComponentPorts.Output);
    public OutputPortHandle<ComponentEvent> Events { get; } = definition.Output<ComponentEvent>(SampleComponentPorts.Events);
}

internal sealed class SinkComponentHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<string> Input { get; } = definition.Input<string>(SampleComponentPorts.Input);
    public OutputPortHandle<ComponentEvent> Events { get; } = definition.Output<ComponentEvent>(SampleComponentPorts.Events);
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

internal sealed class StringSourceNode(IReadOnlyList<string> messages) : FlowSource<string>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(message), cancellationToken)
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

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Value);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}
