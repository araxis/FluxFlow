using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Composition.Authoring;
using FluxFlow.Json.Model;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

var collector = new StringCollector();
var definitionBuilder = new ApplicationDefinitionBuilder()
    .AddWorkflow("main", out var main);

main
    .AddComponent(
        "source",
        SampleComponents.Source,
        options => options.Messages = ["", "skip", "alpha", "beta"],
        out var source)
    .AddComponent(
        "upper",
        SampleComponents.Uppercase,
        out var upper)
    .AddComponent(
        "sink",
        SampleComponents.Sink,
        out var sink);

source.Output.LinkTo(upper.Input, when: static value => !string.IsNullOrWhiteSpace(value.Deserialize<string>()));
sink.Input.LinkFrom(upper.Output, condition: "input != 'SKIP'");
source.Output.DiscardUnmatched();
upper.Output.DiscardUnmatched();
source.Events.DiscardUnmatched();
upper.Events.DiscardUnmatched();
sink.Events.DiscardUnmatched();

var fromJson = args.Contains("--json", StringComparer.Ordinal);
var definition = fromJson
    ? new ApplicationSetup(ApplicationDefinitionJson.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "application.json"))))
    : definitionBuilder.Build();

var services = new ServiceCollection();
services.AddSingleton(collector);
services.AddSingleton<IFlowExpressionEngine, JsonataFlowExpressionEngine>();
services.AddKeyedSingleton<IFlowExpressionEngine>("standard", new JsonataFlowExpressionEngine());
services.AddKeyedSingleton<IFlowExpressionEngine>("rules", new CSharpFlowExpressionEngine(interpreter =>
    interpreter
        .SetFunction("text", (Func<FlowValue, string?>)(value => value.ToJsonElement().GetString()))
        .SetFunction("hasText", (Func<string?, bool>)(value => !string.IsNullOrWhiteSpace(value)))));
if (fromJson)
    services.AddFluxFlowComponents()
        .AddComponent(SampleComponents.Source)
        .AddComponent(SampleComponents.Uppercase)
        .AddComponent(SampleComponents.Sink);

await using var provider = services.BuildServiceProvider();
await using var application = await definition.BuildApplicationAsync(provider);
await application.StartAsync();
await application.Completion.WaitAsync(TimeSpan.FromSeconds(5));

if (!collector.Items.SequenceEqual(["ALPHA", "BETA"]))
    throw new InvalidOperationException("Conditional links must produce only ALPHA and BETA.");

foreach (var item in collector.Items)
{
    Console.WriteLine(item);
}

return 0;

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
    public OutputPortHandle<FlowValue> Output { get; } = definition.Output<FlowValue>(SampleComponentPorts.Output);
    public OutputPortHandle<FlowValue> Events { get; } = definition.Output<FlowValue>(SampleComponentPorts.Events);
}

internal sealed class UppercaseComponentHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<FlowValue> Input { get; } = definition.Input<FlowValue>(SampleComponentPorts.Input);
    public OutputPortHandle<FlowValue> Output { get; } = definition.Output<FlowValue>(SampleComponentPorts.Output);
    public OutputPortHandle<FlowValue> Events { get; } = definition.Output<FlowValue>(SampleComponentPorts.Events);
}

internal sealed class SinkComponentHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<FlowValue> Input { get; } = definition.Input<FlowValue>(SampleComponentPorts.Input);
    public OutputPortHandle<FlowValue> Events { get; } = definition.Output<FlowValue>(SampleComponentPorts.Events);
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

internal sealed class StringSourceNode(IReadOnlyList<string> messages) : FlowSource
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(FlowValue.From(message)), cancellationToken)
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

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        collector.Add(message.Value!.Deserialize<string>()!);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}
