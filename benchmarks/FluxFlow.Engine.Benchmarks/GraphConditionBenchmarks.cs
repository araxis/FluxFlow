using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using BenchmarkDotNet.Attributes;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Json;
using FluxFlow.Json.Model;
using FluxFlow.Mapping;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Benchmarks;

/// <summary>One admitted message traverses the canonical graph and reaches both targets.</summary>
[MemoryDiagnoser]
public class GraphConditionBenchmarks
{
    private readonly FlowMessage _message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    private readonly FlowMessage _negative = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    private FlowApplication _application = null!;
    private FlowInputEndpoint _input = null!;
    private readonly BufferSink _first = new();
    private readonly BufferSink _second = new();
    private readonly BufferSink _fallback = new();

    [Params(1, 8, 32)]
    public int HopCount { get; set; }

    [GlobalSetup(Target = nameof(Unconditional))]
    public Task SetupUnconditional() => SetupNativeAsync(null);

    [GlobalSetup(Target = nameof(NativePredicate))]
    public Task SetupNativePredicate() => SetupNativeAsync(static message => Positive(message.Value!));

    [GlobalSetup(Target = nameof(DocumentExpression))]
    public Task SetupDocumentExpression() => SetupDocumentAsync(new JsonataFlowExpressionEngine(), "input > 0");

    [GlobalSetup(Target = nameof(ConfiguredExpression))]
    public Task SetupConfiguredExpression() => SetupDocumentAsync(
        new CSharpFlowExpressionEngine(interpreter => interpreter.SetFunction("positive", (Func<FlowValue, bool>)Positive)),
        "positive(input)");

    private static bool Positive(FlowValue value) => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0;

    private async Task SetupNativeAsync(Predicate<FlowMessage>? condition)
    {
        _application = new FlowApplication();
        var workflow = _application.AddWorkflow("main");
        var first = workflow.Add("first", _first);
        var second = workflow.Add("second", _second);
        var fallback = workflow.Add("fallback", _fallback);
        var hops = Enumerable.Range(0, HopCount).Select(index => workflow.Add($"hop{index}", new ForwardNode())).ToArray();
        _input = hops[0].Input;
        for (var index = 0; index < hops.Length; index++)
        {
            var output = hops[index].Output;
            if (index + 1 < hops.Length) output.LinkTo(hops[index + 1].Input, condition);
            else
            {
                output.LinkTo(first.Input, condition);
                output.LinkTo(second.Input, condition);
            }
            output.OtherwiseTo(fallback.Input);
        }
        await StartAndProbeAsync(condition is not null);
    }

    private async Task SetupDocumentAsync(IFlowExpressionEngine engine, string condition)
    {
        var components = new Dictionary<string, ComponentDefinition>
        {
            ["first"] = new("first"),
            ["second"] = new("second"),
            ["fallback"] = new("fallback")
        };
        for (var index = 0; index < HopCount; index++)
        {
            var targets = index + 1 < HopCount ? new[] { $"hop{index + 1}.Input" } : ["first.Input", "second.Input"];
            var links = targets.Select(target => (object)new { Port = target, Condition = condition }).ToList();
            links.Add(new { Port = "fallback.Input", Fallback = true });
            components.Add($"hop{index}", new ComponentDefinition("forward",
                [new("Output", JsonSerializer.SerializeToElement(links))]));
        }
        var bindings = new ComponentBindings();
        bindings.Add("forward", static _ => new ForwardNode());
        bindings.Add("first", _ => _first);
        bindings.Add("second", _ => _second);
        bindings.Add("fallback", _ => _fallback);
        var definition = new ApplicationDefinition(workflows: [new("main", new WorkflowDefinition(components))]);
        _application = await FlowJson.BuildAsync(FlowJson.Serialize(definition), bindings, engine);
        _input = _application.GetInput("main.hop0.Input");
        await StartAndProbeAsync(conditional: true);
    }

    private async Task StartAndProbeAsync(bool conditional)
    {
        await _application.StartAsync();
        await VerifyDeliveryAsync(_message);
        if (conditional)
        {
            if (!await _application.SendAsync(_input, _negative))
                throw new InvalidOperationException("The negative probe was not admitted.");
            var rejected = await _fallback.Block.ReceiveAsync(TimeSpan.FromSeconds(30));
            if (!ReferenceEquals(rejected, _negative) || _first.Block.Count != 0 || _second.Block.Count != 0)
                throw new InvalidOperationException("A false condition must route only the original message to fallback.");
        }
        else await VerifyDeliveryAsync(_negative);
        // A following accepted message also verifies that rejection does not hold the route head.
        await VerifyDeliveryAsync(_message);
        if (_first.Block.Count != 0 || _second.Block.Count != 0 || _fallback.Block.Count != 0)
            throw new InvalidOperationException("The setup probes left unexpected deliveries.");
    }

    private async Task VerifyDeliveryAsync(FlowMessage message)
    {
        if (!await _application.SendAsync(_input, message))
            throw new InvalidOperationException("The delivery probe was not admitted.");
        var first = await _first.Block.ReceiveAsync(TimeSpan.FromSeconds(30));
        var second = await _second.Block.ReceiveAsync(TimeSpan.FromSeconds(30));
        if (!ReferenceEquals(first, message) || !ReferenceEquals(second, message))
            throw new InvalidOperationException("Both targets must receive the original message instance.");
    }

    [Benchmark(Baseline = true)]
    public Task<FlowMessage> Unconditional() => DeliverAsync();

    [Benchmark]
    public Task<FlowMessage> NativePredicate() => DeliverAsync();

    [Benchmark]
    public Task<FlowMessage> DocumentExpression() => DeliverAsync();

    [Benchmark]
    public Task<FlowMessage> ConfiguredExpression() => DeliverAsync();

    private async Task<FlowMessage> DeliverAsync()
    {
        if (!await _application.SendAsync(_input, _message))
            throw new InvalidOperationException("The benchmark message was not admitted.");
        await _first.Block.ReceiveAsync();
        return await _second.Block.ReceiveAsync();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        if (_application is not null)
            await _application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class ForwardNode : FlowNode
    {
        protected override async Task ProcessAsync(FlowMessage message) => await EmitAsync(message, Stopping);
    }

    private sealed class BufferSink : IFlowComponent
    {
        internal BufferBlock<FlowMessage> Block { get; } = new(new DataflowBlockOptions { BoundedCapacity = 128 });
        public FlowPorts Ports => new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = Block });
        public Task Completion => Block.Completion;
        public void Complete() => Block.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)Block).Fault(exception);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }
}
