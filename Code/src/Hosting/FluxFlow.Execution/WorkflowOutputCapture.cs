using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Execution;

internal sealed class WorkflowOutputCapture : IAsyncDisposable
{
    private readonly RunObservations _observations;
    private readonly ILogger _logger;
    private readonly string? _instanceId;
    private const int ProbeCapacity = 128;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<FlowOutputProbe> _probes = [];
    private readonly List<Task> _readers = [];
    private bool _disposed;

    public WorkflowOutputCapture(RunObservations observations, ILogger logger, string? instanceId = null) { _observations = observations; _logger = logger; _instanceId = instanceId; }

    public void Attach(IServiceProvider services)
    {
        foreach (var address in services.GetOutputPortAddresses())
        {
            var probe = services.ObserveOutput(address, ProbeCapacity);
            _probes.Add(probe);
            _readers.Add(ReadAsync(probe));
        }
    }

    private async Task ReadAsync(FlowOutputProbe probe)
    {
        long reportedDrops = 0;
        try
        {
            while (await probe.Messages.OutputAvailableAsync(_stop.Token).ConfigureAwait(false))
            {
                Drain(probe);
                ReportDrops();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            using var scope = _logger.BeginScope(_observations.Diagnostics);
            _logger.LogError(error, "Output observation ended for {Port}.", probe.Address.Value);
        }
        finally { Drain(probe); ReportDrops(); }

        void ReportDrops()
        {
            var dropped = probe.DroppedCount;
            _observations.Outputs.AddDropped(dropped - reportedDrops);
            reportedDrops = dropped;
        }
    }

    private void Drain(FlowOutputProbe probe)
    {
        // Yield to cancellation even if a faulty producer continues during shutdown.
        for (var i = 0; i < ProbeCapacity && probe.Messages.TryReceive(out var message); i++)
        {
            try
            {
                _observations.Outputs.Append(probe.Address.Value,
                    JsonSerializer.SerializeToElement(message, FlowJsonContext.Default.FlowMessage), instanceId: _instanceId);
            }
            catch (Exception error) { _observations.Outputs.Append(probe.Address.Value, null, error.ToString(), _instanceId); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        // The host has stopped producing. Drain accepted samples before detaching probes.
        _stop.Cancel();
        await Task.WhenAll(_readers).ConfigureAwait(false);
        foreach (var probe in _probes)
        {
            probe.Dispose();
        }
        _probes.Clear();
        _readers.Clear();
        _stop.Dispose();
        _disposed = true;
    }
}
