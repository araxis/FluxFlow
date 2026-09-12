using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class ApplicationTopologyBenchmarks
{
    private static readonly FlowMessage<string> Message = FlowMessage.Create("benchmark-message");

    private BenchmarkApplication _application = null!;

    [Params(1, 8, 32)]
    public int HopCount { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
        => _application = await BenchmarkApplication.StartAsync(HopCount);

    [GlobalCleanup]
    public async Task CleanupAsync()
        => await _application.DisposeAsync();

    [Benchmark]
    public Task<PortRequestResult<string>> TypedRequestThroughPipeline()
        => _application.Application.Ports.SendAndReceiveAsync(
            _application.RequestInput,
            _application.ReplyOutput,
            Message);
}
