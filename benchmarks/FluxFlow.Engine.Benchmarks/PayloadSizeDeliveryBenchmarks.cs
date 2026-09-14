using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using BenchmarkDotNet.Attributes;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Benchmarks;

[MemoryDiagnoser]
public class PayloadSizeDeliveryBenchmarks
{
    private FlowApplication _application = null!;
    private FlowInputEndpoint _input = null!;
    private FlowMessage _message = null!;
    private BufferBlock<FlowMessage>[] _targets = null!;
    private Action<FlowValue> _verifyPayload = null!;

    [Params(1, 8, 32)]
    public int HopCount { get; set; }

    [Params(1, 2, 8)]
    public int Fanout { get; set; }

    [Params(1024, 1048576)]
    public int PayloadBytes { get; set; }

    [GlobalSetup(Target = nameof(ForwardStructuredValue))]
    public Task SetupStructuredAsync()
    {
        var body = new string('x', PayloadBytes);
        var readings = Enumerable.Range(0, 16).ToArray();
        _message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new StructuredPayload(body, readings, 42), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Array.Fill(readings, -1);
        _verifyPayload = value =>
        {
            var restored = value.Json.Deserialize<StructuredPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (restored is null || restored.Body != body || restored.Sequence != 42 ||
                !restored.Readings.SequenceEqual(Enumerable.Range(0, 16)))
                throw new InvalidOperationException("Structured payload ownership or content changed.");
        };
        return SetupGraphAsync();
    }

    [GlobalSetup(Target = nameof(ForwardBinaryContent))]
    public Task SetupBinaryAsync()
    {
        var bytes = new byte[PayloadBytes];
        new Random(42).NextBytes(bytes);
        var expected = bytes.ToArray();
        _message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(FlowContent.FromBytes(bytes, "application/octet-stream"), new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Array.Clear(bytes);
        _verifyPayload = value =>
        {
            var restored = value.Json.Deserialize<FlowContent>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (restored is null || restored.ContentType != "application/octet-stream" ||
                restored.Encoding is not null || !restored.Bytes.AsSpan().SequenceEqual(expected))
                throw new InvalidOperationException("Binary payload ownership or exact bytes changed.");
        };
        return SetupGraphAsync();
    }

    private async Task SetupGraphAsync()
    {
        _application = new FlowApplication();
        var workflow = _application.AddWorkflow("main");
        var components = Enumerable.Range(0, HopCount)
            .Select(index => workflow.Add($"hop{index}", new ForwardNode())).ToArray();
        _input = components[0].Input;
        for (var index = 1; index < components.Length; index++)
            components[index - 1].Output.LinkTo(components[index].Input);
        _targets = Enumerable.Range(0, Fanout)
            .Select(_ => new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 128 }))
            .ToArray();
        for (var index = 0; index < _targets.Length; index++)
            components[^1].Output.LinkTo(workflow.Add($"target{index}", new BufferSink(_targets[index])).Input);
        await _application.StartAsync();
        await VerifyDeliveryAsync();
    }

    private async Task VerifyDeliveryAsync()
    {
        if (!await _application.SendAsync(_input, _message))
            throw new InvalidOperationException("The application rejected the verification message.");
        foreach (var target in _targets)
        {
            var received = await target.ReceiveAsync(TimeSpan.FromSeconds(30));
            if (received.IsError || received.Value is not { } value ||
                !ReferenceEquals(received, _message) || !ReferenceEquals(value, _message.Value))
                throw new InvalidOperationException("Forwarding must share message and payload instances.");
            _verifyPayload(value);
        }
    }

    [Benchmark]
    public Task<FlowMessage> ForwardStructuredValue() => ForwardAsync();

    [Benchmark]
    public Task<FlowMessage> ForwardBinaryContent() => ForwardAsync();

    private async Task<FlowMessage> ForwardAsync()
    {
        if (!await _application.SendAsync(_input, _message))
            throw new InvalidOperationException("The application rejected the benchmark message.");
        FlowMessage received = null!;
        foreach (var target in _targets)
            received = await target.ReceiveAsync();
        return received;
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        try
        {
            await VerifyDeliveryAsync();
            await _application.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { await _application.DisposeAsync(); }
    }

    private sealed record StructuredPayload(string Body, int[] Readings, int Sequence);

    private sealed class ForwardNode : FlowNode
    {
        protected override async Task ProcessAsync(FlowMessage message) => await EmitAsync(message);
    }

    private sealed class BufferSink : IFlowComponent
    {
        private readonly BufferBlock<FlowMessage> _input;

        public BufferSink(BufferBlock<FlowMessage> input)
        {
            _input = input;
            Ports = new FlowPorts(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = input });
        }

        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_input).Fault(exception);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }
}
