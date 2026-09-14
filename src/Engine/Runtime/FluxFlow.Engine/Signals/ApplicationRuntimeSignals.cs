using System.Text.Json;
using System.Collections.Immutable;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Signals;

internal sealed class ApplicationRuntimeSignals : IDisposable, IAsyncDisposable
{
    internal const int Capacity = 256;

    private readonly FlowOutput<FlowMessage> _systemEvents;
    private readonly FlowOutput<FlowMessage> _diagnostics = new(
        new() { Capacity = Capacity }, capacityIncludesActive: false, isolateTargetFailures: true);
    private readonly FlowOutput<FlowMessage> _activity = new(
        new() { Capacity = Capacity }, capacityIncludesActive: false, isolateTargetFailures: true);
    private readonly ILogger? _logger;
    private int _stopped;

    public ApplicationRuntimeSignals(ILogger? logger)
    {
        _logger = logger;
        _systemEvents = new FlowOutput<FlowMessage>(
            new() { Capacity = Capacity }, capacityIncludesActive: false, isolateTargetFailures: true,
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
        _ = message.ToFlowEvent();
        cancellationToken.ThrowIfCancellationRequested();

        EventObserver?.Invoke(message);

        if (Volatile.Read(ref _stopped) != 0)
        {
            ApplicationRuntimeInstrumentation.RecordSystemEvent(message, accepted: false);
            return new SystemEventPublishResult { Status = SystemEventPublishStatus.Completed };
        }

        var accepted = await _systemEvents
            .SendAsync(message, cancellationToken)
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
        return TryPublishDiagnostic(message, message.ToFlowEvent());
    }

    internal bool TryPublishDiagnostic(
        FlowEvent diagnostic,
        CorrelationId? correlationId = null,
        TraceId? traceId = null,
        MessageId? causationId = null,
        ApplicationAddress? source = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic.Name);
        // Instrumentation must see the same values as the encoded event, even if
        // an observer changes the caller's measurement dictionary during publication.
        var snapshot = diagnostic with
        {
            Name = diagnostic.Name.Trim(),
            Measurements = diagnostic.Measurements.ToImmutableDictionary(StringComparer.Ordinal)
        };
        var message = ApplicationSignalMessage.Create(snapshot, correlationId, traceId, causationId, source);
        return TryPublishDiagnostic(message, snapshot);
    }

    private bool TryPublishDiagnostic(FlowMessage message, FlowEvent diagnostic)
    {
        EventObserver?.Invoke(message);

        if (Volatile.Read(ref _stopped) != 0 || !_diagnostics.Post(message))
        {
            ApplicationRuntimeInstrumentation.RecordDiagnosticDrop();
            return false;
        }

        ApplicationRuntimeInstrumentation.RecordDiagnostic(message, diagnostic, _logger);
        _activity.Post(message);
        return true;
    }

    internal bool TryPublishActivity(FlowMessage message)
        => Volatile.Read(ref _stopped) == 0 && _activity.Post(message);

    public void Complete()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _systemEvents.Complete();
        _diagnostics.Complete();
        _activity.Complete();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _stopped, 1);
        // Cancel every stream before awaiting any outstanding reserved offer.
        _systemEvents.Abort();
        _diagnostics.Abort();
        _activity.Abort();
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
            Details = (error)
        };
        TryPublishDiagnostic(diagnostic);
    }
    private static readonly JsonSerializerOptions ValueJsonOptions = new(JsonSerializerDefaults.Web);

}
