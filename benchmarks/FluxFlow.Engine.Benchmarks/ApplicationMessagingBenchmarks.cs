using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class ApplicationMessagingBenchmarks
{
    private static readonly FlowMessage<string> Message = FlowMessage.Create("benchmark-message");

    private BenchmarkApplication _unconditional = null!;
    private BenchmarkApplication _conditional = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _unconditional = await BenchmarkApplication.StartAsync(hopCount: 2);
        _conditional = await BenchmarkApplication.StartAsync(
            hopCount: 2,
            static value => value.Length > 0);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await _conditional.DisposeAsync();
        await _unconditional.DisposeAsync();
    }

    [Benchmark(Baseline = true)]
    public Task<PortRequestResult<string>> AddressedUnconditionalRequest()
        => _unconditional.Application.Ports.SendAndReceiveAsync<string, string>(
            _unconditional.Input.Input.Address,
            _unconditional.Output.Output.Address,
            Message);

    [Benchmark]
    public Task<PortRequestResult<string>> TypedUnconditionalRequest()
        => _unconditional.Application.Ports.SendAndReceiveAsync(
            _unconditional.Input.Input,
            _unconditional.Output.Output,
            Message);

    [Benchmark]
    public Task<PortRequestResult<string>> TypedConditionalRequest()
        => _conditional.Application.Ports.SendAndReceiveAsync(
            _conditional.Input.Input,
            _conditional.Output.Output,
            Message);
}
