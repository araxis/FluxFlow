using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Engine.Benchmarks;

[MemoryDiagnoser]
public class PayloadSizeDeliveryBenchmarks
{
    private ServiceProvider _provider = null!;
    private FlowApplication _applicationExecution = null!;
    private ITargetBlock<FlowMessage> _input = null!;
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
        _message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { formatVersion = 1, bytes, contentType = "application/octet-stream", encoding = (string?)null }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Array.Clear(bytes);
        _verifyPayload = value =>
        {
            var restored = value.Json;
            if (restored.GetProperty("contentType").GetString() != "application/octet-stream" ||
                restored.GetProperty("encoding").ValueKind != JsonValueKind.Null ||
                !restored.GetProperty("bytes").GetBytesFromBase64().AsSpan().SequenceEqual(expected))
                throw new InvalidOperationException("Binary payload ownership or exact bytes changed.");
        };
        return SetupGraphAsync();
    }

    private async Task SetupGraphAsync()
    {
        var nodes = Enumerable.Range(0, HopCount).Select(_ => new ForwardNode()).ToArray();
        _targets = Enumerable.Range(0, Fanout)
            .Select(_ => new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 128 }))
            .ToArray();
        var services = new ServiceCollection();
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            var address = $"main.hop{index}";
            services.AddComponentFactory(address, _ => node)
                .AddInputPort<ForwardNode>(address, "Input", value => value.Input)
                .AddOutputPort<ForwardNode>(address, "Output", value => value.Output)
                .AddEventPort<ForwardNode>(address, value => value.Events);
        }
        for (var index = 0; index < _targets.Length; index++)
        {
            var target = _targets[index];
            var address = $"main.target{index}";
            services.AddComponentFactory(address, _ => new BufferSink(target))
                .AddInputPort<BufferSink>(address, "Input", value => value.Input);
        }
        services.AddSingleton<LinkFactory>(ports =>
        {
            for (var index = 1; index < nodes.Length; index++)
                ports.Link($"main.hop{index - 1}.Output", $"main.hop{index}.Input");
            for (var index = 0; index < _targets.Length; index++)
                ports.Link($"main.hop{nodes.Length - 1}.Output", $"main.target{index}.Input");
        });
        _provider = services.BuildServiceProvider();
        _applicationExecution = await _provider.BuildApplicationAsync();
        _input = nodes[0].Input;
        await _applicationExecution.StartAsync();
        await VerifyDeliveryAsync();
    }

    private async Task VerifyDeliveryAsync()
    {
        if (!await _input.SendAsync(_message))
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
        if (!await _input.SendAsync(_message))
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
            await _applicationExecution.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            try { await _applicationExecution.DisposeAsync(); }
            finally { await _provider.DisposeAsync(); }
        }
    }

    private sealed record StructuredPayload(string Body, int[] Readings, int Sequence);

    private sealed class ForwardNode : FlowNode
    {
        protected override async Task ProcessAsync(FlowMessage message) => await EmitAsync(message);
    }

    private sealed class BufferSink : IFlowNode
    {
        private readonly BufferBlock<FlowMessage> _input;

        public BufferSink(BufferBlock<FlowMessage> input)
        {
            _input = input;
        }

        public ITargetBlock<FlowMessage> Input => _input;
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_input).Fault(exception);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }
}
