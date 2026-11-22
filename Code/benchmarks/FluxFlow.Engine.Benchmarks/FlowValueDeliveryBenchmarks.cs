using FluxFlow.Application.Addressing;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Engine.Benchmarks;
[MemoryDiagnoser]
public class FlowValueDeliveryBenchmarks
{
    private readonly FlowMessage _message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { value = 42 }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    private ForwardNode[] _nodes = null!;
    private BufferBlock<FlowMessage>[] _targets = null!;
    private readonly List<IDisposable> _links = [];
    private ServiceProvider _provider = null!;
    private FlowApplication _applicationExecution = null!;
    private ITargetBlock<FlowMessage> _applicationInput = null!;
    [Params(1, 8, 32)]
    public int HopCount { get; set; }

    [Params(1, 2, 8)]
    public int Fanout { get; set; }

    [GlobalSetup(Target = nameof(SendThroughPipelineAndFanout))]
    public async Task SetupAsync()
    {
        CreateComponents();
        for (var index = 1; index < _nodes.Length; index++)
            _links.Add(_nodes[index - 1].Output.LinkTo(_nodes[index].Input, new DataflowLinkOptions()));
        foreach (var target in _targets)
            _links.Add(_nodes[^1].Output.LinkTo(target, new DataflowLinkOptions()));
        if (!await _nodes[0].Input.SendAsync(_message))
            throw new InvalidOperationException("The pipeline rejected the setup message.");
        await VerifyTargetsAsync();
    }

    [GlobalSetup(Target = nameof(SendThroughApplicationGraph))]
    public async Task SetupApplicationAsync()
    {
        CreateComponents();
        var services = new ServiceCollection();
        for (var index = 0; index < _nodes.Length; index++)
        {
            var node = _nodes[index];
            var address = $"main.hop{index}";
            services.AddComponentFactory(ComponentAddress.Parse(address), _ => node).AddInputPort<ForwardNode>(ComponentAddress.Parse(address), "Input", value => value.Input).AddOutputPort<ForwardNode>(ComponentAddress.Parse(address), "Output", value => value.Output).AddEventPort<ForwardNode>(ComponentAddress.Parse(address), value => value.Events);
        }

        for (var index = 0; index < _targets.Length; index++)
        {
            var target = _targets[index];
            var address = $"main.target{index}";
            services.AddComponentFactory(ComponentAddress.Parse(address), _ => new BufferSink(target)).AddInputPort<BufferSink>(ComponentAddress.Parse(address), "Input", value => value.Input);
        }

        services.AddSingleton<LinkFactory>(ports =>
        {
            for (var index = 1; index < _nodes.Length; index++)
                ports.Link(PortAddress.Parse($"main.hop{index - 1}.Output"), PortAddress.Parse($"main.hop{index}.Input"));
            for (var index = 0; index < _targets.Length; index++)
                ports.Link(PortAddress.Parse($"main.hop{_nodes.Length - 1}.Output"), PortAddress.Parse($"main.target{index}.Input"));
        });
        _provider = services.BuildServiceProvider();
        _applicationExecution = await _provider.BuildApplicationAsync();
        _applicationInput = _nodes[0].Input;
        await _applicationExecution.StartAsync();
        if (!await _applicationInput.SendAsync(_message))
            throw new InvalidOperationException("The application rejected the setup message.");
        await VerifyTargetsAsync();
    }

    private void CreateComponents()
    {
        _nodes = Enumerable.Range(0, HopCount).Select(_ => new ForwardNode()).ToArray();
        _targets = Enumerable.Range(0, Fanout).Select(_ => new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 128 })).ToArray();
    }

    private async Task VerifyTargetsAsync()
    {
        foreach (var target in _targets)
        {
            var received = await target.ReceiveAsync(TimeSpan.FromSeconds(30));
            if (!ReferenceEquals(received, _message))
                throw new InvalidOperationException("Pass-through delivery must share the message instance.");
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<FlowMessage> SendThroughPipelineAndFanout()
    {
        if (!await _nodes[0].Input.SendAsync(_message))
            throw new InvalidOperationException("The pipeline rejected the benchmark message.");
        FlowMessage received = null!;
        foreach (var target in _targets)
            received = await target.ReceiveAsync();
        return received;
    }

    [Benchmark]
    public async Task<FlowMessage> SendThroughApplicationGraph()
    {
        if (!await _applicationInput.SendAsync(_message))
            throw new InvalidOperationException("The application rejected the benchmark message.");
        FlowMessage received = null!;
        foreach (var target in _targets)
            received = await target.ReceiveAsync();
        return received;
    }

    [GlobalCleanup(Target = nameof(SendThroughApplicationGraph))]
    public async Task CleanupApplicationAsync()
    {
        await _applicationExecution.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            await _applicationExecution.DisposeAsync();
        }
        finally
        {
            await _provider.DisposeAsync();
        }
    }

    [GlobalCleanup(Target = nameof(SendThroughPipelineAndFanout))]
    public async Task CleanupAsync()
    {
        foreach (var node in _nodes)
        {
            node.Complete();
            await node.Completion;
            await node.DisposeAsync();
        }

        foreach (var link in _links)
            link.Dispose();
        _links.Clear();
        foreach (var target in _targets)
        {
            target.Complete();
            await target.Completion;
        }
    }

    private sealed class ForwardNode : FlowNode
    {
        public ITargetBlock<FlowMessage> Input { get; }

        public ForwardNode()
        {
            var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
            var outputCapacityValue = 128;
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
            var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
            Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
            Events = new BroadcastBlock<FlowMessage>(static message => message);
            Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
            var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, ProcessAsync, Output, Stopping), execution);
            input.LinkTo(Output);
            Input = input;
            Lifetime.HasInput(input);
            Lifetime.HasOutput(Output);
            Lifetime.HasEvents(Events);
            Lifetime.Observe();
        }

        private async Task ProcessAsync(FlowMessage message) => await EmitAsync(message);
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
        public async ValueTask DisposeAsync()
        {
            Complete();
            await Completion;
        }
    }
}
