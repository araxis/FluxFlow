using System.Diagnostics;
using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Engine.Ports;

internal sealed class ApplicationPortEventPublisher(
    IReadOnlyDictionary<ApplicationAddress, ApplicationPortMetadata> metadataByAddress,
    ApplicationRuntimeSignals signals,
    Action<ApplicationPortRejection> publishRejection)
{
    internal void ReportRejection(ApplicationPortRejection rejection)
    {
        publishRejection(rejection);
        if (rejection.Port == ApplicationAddress.SystemDiagnostics ||
            rejection.RelatedPort == ApplicationAddress.SystemDiagnostics)
        {
            return;
        }

        signals.TryPublishDiagnostic(CreateDiagnosticMessage(rejection));
        if (!CreatesSystemEvent(rejection) ||
            rejection.Port.Kind == ApplicationAddressKind.SystemPort ||
            rejection.RelatedPort?.Kind == ApplicationAddressKind.SystemPort)
        {
            return;
        }

        signals.PublishSystemEventAsync(
                CreateSystemEventMessage(rejection),
                CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    internal void ReportActivity(ApplicationPortActivity activity)
    {
        if (activity.Port == ApplicationAddress.SystemDiagnostics ||
            activity.RelatedPort == ApplicationAddress.SystemDiagnostics)
        {
            return;
        }

        var diagnostic = new ApplicationDiagnostic
        {
            Timestamp = activity.Timestamp,
            Name = activity.Kind == ApplicationPortActivityKind.InputAccepted
                ? ApplicationDiagnosticNames.InputAccepted
                : ApplicationDiagnosticNames.OutputEmitted,
            Kind = activity.Kind == ApplicationPortActivityKind.InputAccepted
                ? ApplicationDiagnosticKind.Input
                : ApplicationDiagnosticKind.Output,
            Level = ApplicationDiagnosticLevel.Trace,
            Subject = activity.Port.Value,
            Attributes = CreateAttributes(
                ("port", activity.Port.Value),
                ("relatedPort", activity.RelatedPort?.Value))
        };
        signals.TryPublishDiagnostic(FlowMessage.Create(
            diagnostic,
            activity.CorrelationId,
            activity.TraceId,
            causationId: activity.MessageId));
    }

    internal void ReportRequest<TRequest>(
        PortRequestStatus status,
        FlowMessage<TRequest> request,
        ApplicationAddress input,
        ApplicationAddress output,
        long startedAt)
    {
        var diagnostic = new ApplicationDiagnostic
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = ApplicationDiagnosticNames.RequestCompleted,
            Kind = ApplicationDiagnosticKind.Timing,
            Level = status == PortRequestStatus.Received
                ? ApplicationDiagnosticLevel.Debug
                : ApplicationDiagnosticLevel.Warning,
            Subject = input.Value,
            Duration = Stopwatch.GetElapsedTime(startedAt),
            Attributes = CreateAttributes(
                ("input", input.Value),
                ("output", output.Value),
                ("status", status.ToString()))
        };
        signals.TryPublishDiagnostic(request.With(diagnostic));
    }

    private FlowMessage<ApplicationDiagnostic> CreateDiagnosticMessage(
        ApplicationPortRejection rejection)
    {
        var diagnostic = new ApplicationDiagnostic
        {
            Timestamp = rejection.Timestamp,
            Name = ApplicationDiagnosticNames.PortRejected,
            Kind = metadataByAddress.TryGetValue(rejection.Port, out var metadata) &&
                metadata.Direction == ApplicationPortDirection.Input
                    ? ApplicationDiagnosticKind.Input
                    : ApplicationDiagnosticKind.Output,
            Level = rejection.Reason is ApplicationPortRejectionReason.ConditionFailed or
                ApplicationPortRejectionReason.SourceFaulted or
                ApplicationPortRejectionReason.ComponentFaulted
                    ? ApplicationDiagnosticLevel.Error
                    : ApplicationDiagnosticLevel.Warning,
            Subject = rejection.Port.Value,
            Message = $"Port activity was rejected with reason '{rejection.Reason}'.",
            Error = rejection.Exception is null
                ? null
                : CreateFlowError(rejection),
            Attributes = CreateAttributes(
                ("port", rejection.Port.Value),
                ("relatedPort", rejection.RelatedPort?.Value),
                ("reason", rejection.Reason.ToString()))
        };
        return CreateSignalMessage(
            diagnostic,
            rejection.CorrelationId,
            rejection.TraceId,
            rejection.MessageId);
    }

    private static FlowMessage<ApplicationSystemEvent> CreateSystemEventMessage(
        ApplicationPortRejection rejection)
    {
        var systemEvent = new ApplicationSystemEvent
        {
            Timestamp = rejection.Timestamp,
            Name = rejection.Reason switch
            {
                ApplicationPortRejectionReason.ConditionFailed =>
                    ApplicationSystemEventNames.LinkConditionFailed,
                ApplicationPortRejectionReason.SourceFaulted =>
                    ApplicationSystemEventNames.ComponentFaulted,
                ApplicationPortRejectionReason.ComponentFaulted =>
                    ApplicationSystemEventNames.ComponentFaulted,
                _ => ApplicationSystemEventNames.LinkTargetRejected
            },
            Category = rejection.Reason is ApplicationPortRejectionReason.SourceFaulted or
                ApplicationPortRejectionReason.ComponentFaulted
                    ? ApplicationSystemEventCategory.Component
                    : ApplicationSystemEventCategory.Link,
            Subject = rejection.Port.Value,
            Error = CreateFlowError(rejection),
            Details = CreateDetails(
                ("port", rejection.Port.Value),
                ("relatedPort", rejection.RelatedPort?.Value),
                ("reason", rejection.Reason.ToString()))
        };
        return CreateSignalMessage(
            systemEvent,
            rejection.CorrelationId,
            rejection.TraceId,
            rejection.MessageId);
    }

    private static bool CreatesSystemEvent(ApplicationPortRejection rejection)
        => rejection.Reason is ApplicationPortRejectionReason.ConditionFailed or
            ApplicationPortRejectionReason.TargetRejected or
            ApplicationPortRejectionReason.SourceFaulted or
            ApplicationPortRejectionReason.ComponentFaulted;

    private static DataFlowError CreateFlowError(ApplicationPortRejection rejection)
        => new(
            $"runtime.{rejection.Reason.ToString().ToLowerInvariant()}",
            rejection.Exception?.Message ?? $"Runtime port failure: {rejection.Reason}.",
            rejection.Reason is ApplicationPortRejectionReason.SourceFaulted or
                ApplicationPortRejectionReason.ComponentFaulted
                    ? "component"
                    : "link",
            isTransient: rejection.Reason != ApplicationPortRejectionReason.ConditionFailed,
            CreateDetails(
                ("port", rejection.Port.Value),
                ("relatedPort", rejection.RelatedPort?.Value)));

    private static FlowMessage<T> CreateSignalMessage<T>(
        T payload,
        CorrelationId? correlationId,
        TraceId? traceId,
        MessageId? causationId)
        => FlowMessage.Create(
            payload,
            correlationId is { IsEmpty: false } ? correlationId : null,
            traceId is { IsEmpty: false } ? traceId : null,
            causationId: causationId);

    private static IReadOnlyDictionary<string, string> CreateAttributes(
        params (string Name, string? Value)[] values)
        => values
            .Where(static value => value.Value is not null)
            .ToDictionary(
                value => value.Name,
                value => value.Value!,
                StringComparer.Ordinal);

    private static JsonElement CreateDetails(params (string Name, string? Value)[] values)
        => JsonSerializer.SerializeToElement(values
            .Where(static value => value.Value is not null)
            .ToDictionary(
                value => value.Name,
                value => value.Value!,
                StringComparer.Ordinal));
}
