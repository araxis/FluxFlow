using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Composition.Definition;
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
    .AddSource("source",
        options => options.Messages = ["", "skip", "alpha", "beta"],
        out var source)
    .AddUppercase("upper",
        out var upper)
    .AddSink("sink",
        out var sink);

source.Output.LinkTo(upper.Input, when: static value => !string.IsNullOrWhiteSpace(value.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))));
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
        .SetFunction("text", (Func<FlowValue, string?>)(value => value.Json.GetString()))
        .SetFunction("hasText", (Func<string?, bool>)(value => !string.IsNullOrWhiteSpace(value)))));
if (fromJson)
    services
        .AddComponent(SampleComponentTypes.Source, SampleComponents.CreateSource)
        .AddComponent(SampleComponentTypes.Uppercase, SampleComponents.CreateUppercase)
        .AddComponent(SampleComponentTypes.Sink, SampleComponents.CreateSink);

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
    public static ValueTask<IFlowNode> CreateSource(ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SourceOptions>();
        var createdNode = new StringSourceNode(options.Messages);
        context.HasOutput(SampleComponentPorts.Output, createdNode.Output);
        context.HasEvents(SampleComponentPorts.Events, createdNode.Events);
        return ValueTask.FromResult<IFlowNode>(createdNode);
    }

    public static SourceComponentHandle AddSource(this WorkflowDefinitionBuilder workflow, string name, Action<SourceComponentBuilder> configure)
        => workflow.AddComponent(name, SampleComponentTypes.Source, CreateSource,
            definition => { var options = new SourceComponentBuilder(); configure(options); options.Apply(definition); }, static component => new SourceComponentHandle(component));

    public static WorkflowDefinitionBuilder AddSource(this WorkflowDefinitionBuilder workflow, string name, Action<SourceComponentBuilder> configure, out SourceComponentHandle handle)
    {
        handle = workflow.AddSource(name, configure);
        return workflow;
    }

    public static ValueTask<IFlowNode> CreateUppercase(ComponentActivationContext context)
    {
        var createdNode = new UppercaseNode();
        context.HasInput(SampleComponentPorts.Input, createdNode.Input);
        context.HasOutput(SampleComponentPorts.Output, createdNode.Output);
        context.HasEvents(SampleComponentPorts.Events, createdNode.Events);
        return ValueTask.FromResult<IFlowNode>(createdNode);
    }

    public static UppercaseComponentHandle AddUppercase(this WorkflowDefinitionBuilder workflow, string name)
        => workflow.AddComponent(name, SampleComponentTypes.Uppercase, CreateUppercase,
            static _ => { }, static component => new UppercaseComponentHandle(component));

    public static WorkflowDefinitionBuilder AddUppercase(this WorkflowDefinitionBuilder workflow, string name, out UppercaseComponentHandle handle)
    {
        handle = workflow.AddUppercase(name);
        return workflow;
    }

    public static ValueTask<IFlowNode> CreateSink(ComponentActivationContext context)
    {
        var createdNode = new CollectSinkNode(
                        context.Services.GetRequiredService<StringCollector>());
        context.HasInput(SampleComponentPorts.Input, createdNode.Input);
        context.HasEvents(SampleComponentPorts.Events, createdNode.Events);
        return ValueTask.FromResult<IFlowNode>(createdNode);
    }

    public static SinkComponentHandle AddSink(this WorkflowDefinitionBuilder workflow, string name)
        => workflow.AddComponent(name, SampleComponentTypes.Sink, CreateSink,
            static _ => { }, static component => new SinkComponentHandle(component));

    public static WorkflowDefinitionBuilder AddSink(this WorkflowDefinitionBuilder workflow, string name, out SinkComponentHandle handle)
    {
        handle = workflow.AddSink(name);
        return workflow;
    }
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
            await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

internal sealed class UppercaseNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        await EmitAsync(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value!.Json.Deserialize<string>()!.ToUpperInvariant(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Stopping)
            .ConfigureAwait(false);
    }
}

internal sealed class CollectSinkNode(StringCollector collector) : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
    {
        collector.Add(message.Value!.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}
