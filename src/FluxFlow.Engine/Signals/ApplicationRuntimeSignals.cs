using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Engine.Components;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Signals;

internal sealed class ApplicationRuntimeSignals : IDisposable, IAsyncDisposable
{
    internal const int Capacity = 256;

    private readonly FlowFanoutSource<FlowMessage<ApplicationSystemEvent>> _systemEvents;
    private readonly FlowFanoutSource<FlowMessage<ApplicationDiagnostic>> _diagnostics = new();
    private readonly ILogger? _logger;
    private int _stopped;

    public ApplicationRuntimeSignals(ILogger? logger)
    {
        _logger = logger;
        _systemEvents = new FlowFanoutSource<FlowMessage<ApplicationSystemEvent>>(
            deliveryFailure: ReportSystemEventDeliveryFailure);
    }

    public ISourceBlock<FlowMessage<ApplicationSystemEvent>> SystemEvents => _systemEvents;

    public ISourceBlock<FlowMessage<ApplicationDiagnostic>> Diagnostics => _diagnostics;

    public Task Completion => Task.WhenAll(_systemEvents.Completion, _diagnostics.Completion);

    public async ValueTask<SystemEventPublishResult> PublishSystemEventAsync(
        FlowMessage<ApplicationSystemEvent> message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateSystemEvent(message.Payload);
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _stopped) != 0)
        {
            ApplicationRuntimeInstrumentation.RecordSystemEvent(message, accepted: false);
            return new SystemEventPublishResult { Status = SystemEventPublishStatus.Completed };
        }

        var accepted = await _systemEvents
            .SendWithBackpressureAsync(message, cancellationToken)
            .ConfigureAwait(false);
        ApplicationRuntimeInstrumentation.RecordSystemEvent(message, accepted);
        return new SystemEventPublishResult
        {
            Status = accepted
                ? SystemEventPublishStatus.Accepted
                : SystemEventPublishStatus.Completed
        };
    }

    public bool TryPublishDiagnostic(FlowMessage<ApplicationDiagnostic> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateDiagnostic(message.Payload);

        if (Volatile.Read(ref _stopped) != 0 || !_diagnostics.Post(message))
        {
            ApplicationRuntimeInstrumentation.RecordDiagnosticDrop();
            return false;
        }

        ApplicationRuntimeInstrumentation.RecordDiagnostic(message, _logger);
        return true;
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _systemEvents.Complete();
        _diagnostics.Complete();
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _stopped, 1);
        _systemEvents.Dispose();
        _diagnostics.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stopped, 1);
        await _systemEvents.DisposeAsync().ConfigureAwait(false);
        await _diagnostics.DisposeAsync().ConfigureAwait(false);
    }

    private void ReportSystemEventDeliveryFailure(Exception exception)
    {
        var error = new FluxFlow.Data.FlowError(
            "runtime.system_event.delivery_failed",
            exception.Message,
            "runtime",
            isTransient: true);
        var diagnostic = new ApplicationDiagnostic
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = ApplicationDiagnosticNames.SystemEventDeliveryFailed,
            Kind = ApplicationDiagnosticKind.Runtime,
            Level = ApplicationDiagnosticLevel.Warning,
            Message = "A system-event subscriber stopped accepting messages and was detached.",
            Error = error
        };
        TryPublishDiagnostic(FlowMessage.Create(diagnostic));
    }

    private static void ValidateSystemEvent(ApplicationSystemEvent systemEvent)
    {
        ArgumentNullException.ThrowIfNull(systemEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemEvent.Name);
    }

    private static void ValidateDiagnostic(ApplicationDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic.Name);
    }
}
