using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Signals;

internal sealed class ApplicationRuntimeSignals : IDisposable, IAsyncDisposable
{
    internal const int Capacity = 256;

    private readonly FlowFanoutSource<FlowMessage> _systemEvents;
    private readonly FlowFanoutSource<FlowMessage> _diagnostics = new();
    private readonly FlowFanoutSource<FlowMessage> _activity = new();
    private readonly ILogger? _logger;
    private int _stopped;

    public ApplicationRuntimeSignals(ILogger? logger)
    {
        _logger = logger;
        _systemEvents = new FlowFanoutSource<FlowMessage>(
            deliveryFailure: ReportSystemEventDeliveryFailure);
    }

    public ISourceBlock<FlowMessage> SystemEvents => _systemEvents;

    public ISourceBlock<FlowMessage> Diagnostics => _diagnostics;

    public ISourceBlock<FlowMessage> Activity => _activity;

    internal Action<FlowMessage>? EventObserver { get; set; }

    public Task Completion => Task.WhenAll(
        _systemEvents.Completion,
        _diagnostics.Completion,
        _activity.Completion);

    internal async ValueTask<SystemEventPublishResult> PublishSystemEventAsync(
        FlowMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateActivity(message);
        cancellationToken.ThrowIfCancellationRequested();

        EventObserver?.Invoke(message);

        if (Volatile.Read(ref _stopped) != 0)
        {
            ApplicationRuntimeInstrumentation.RecordSystemEvent(message, accepted: false);
            return new SystemEventPublishResult { Status = SystemEventPublishStatus.Completed };
        }

        var accepted = await _systemEvents
            .SendWithBackpressureAsync(message, cancellationToken)
            .ConfigureAwait(false);
        ApplicationRuntimeInstrumentation.RecordSystemEvent(message, accepted);
        if (accepted)
            _activity.Post(message);
        return new SystemEventPublishResult
        {
            Status = accepted
                ? SystemEventPublishStatus.Accepted
                : SystemEventPublishStatus.Completed
        };
    }

    internal bool TryPublishDiagnostic(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateActivity(message);

        EventObserver?.Invoke(message);

        if (Volatile.Read(ref _stopped) != 0 || !_diagnostics.Post(message))
        {
            ApplicationRuntimeInstrumentation.RecordDiagnosticDrop();
            return false;
        }

        ApplicationRuntimeInstrumentation.RecordDiagnostic(message, _logger);
        _activity.Post(message);
        return true;
    }

    public IDisposable AttachActivity(ISourceBlock<FlowMessage> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sink = new ActionBlock<FlowMessage>(message => _activity.Post(message));
        return source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _systemEvents.Complete();
        _diagnostics.Complete();
        _activity.Complete();
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _stopped, 1);
        _systemEvents.Dispose();
        _diagnostics.Dispose();
        _activity.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stopped, 1);
        await _systemEvents.DisposeAsync().ConfigureAwait(false);
        await _diagnostics.DisposeAsync().ConfigureAwait(false);
        await _activity.DisposeAsync().ConfigureAwait(false);
    }

    private void ReportSystemEventDeliveryFailure(Exception exception)
    {
        var error = new FluxFlow.Data.FlowError(
            "runtime.system_event.delivery_failed",
            exception.Message,
            "runtime",
            isTransient: true);
        var diagnostic = new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = ApplicationDiagnosticNames.SystemEventDeliveryFailed,
            Kind = FlowEventKind.Diagnostic,
            Level = FlowEventLevel.Warning,
            Message = "A system-event subscriber stopped accepting messages and was detached.",
            Details = FlowValue.From(error)
        };
        TryPublishDiagnostic(ApplicationSignalMessage.Create(diagnostic));
    }

    private static void ValidateActivity(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _ = message.ToFlowEvent();
    }
}
