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

        var diagnostic = new FlowEvent
        {
            Timestamp = activity.Timestamp,
            Name = activity.Kind == ApplicationPortActivityKind.InputAccepted
                ? ApplicationDiagnosticNames.InputAccepted
                : ApplicationDiagnosticNames.OutputEmitted,
            Kind = FlowEventKind.Diagnostic,
            Level = FlowEventLevel.Trace,
            Dimensions = CreateAttributes(
                ("kind", activity.Kind == ApplicationPortActivityKind.InputAccepted ? "Input" : "Output"),
                ("port", activity.Port.Value),
                ("relatedPort", activity.RelatedPort?.Value))
        };
        signals.TryPublishDiagnostic(ApplicationSignalMessage.Create(
            diagnostic,
            activity.CorrelationId,
            activity.TraceId,
            activity.MessageId,
            activity.Port));
    }

    internal void ReportRequest<TRequest>(
        PortRequestStatus status,
        FlowMessage<TRequest> request,
        ApplicationAddress input,
        ApplicationAddress output,
        long startedAt)
    {
        var diagnostic = new FlowEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Name = ApplicationDiagnosticNames.RequestCompleted,
            Kind = FlowEventKind.Diagnostic,
            Level = status == PortRequestStatus.Received
                ? FlowEventLevel.Trace
                : FlowEventLevel.Warning,
            Dimensions = CreateAttributes(
                ("kind", "Timing"),
                ("subject", input.Value),
                ("status", status.ToString())),
            Measurements = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["duration.ms"] = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
            },
            Attributes = CreateAttributes(
                ("input", input.Value),
                ("output", output.Value),
                ("status", status.ToString()))
        };
        signals.TryPublishDiagnostic(ApplicationSignalMessage.Create(
            diagnostic,
            request.CorrelationId,
            request.TraceId,
            request.MessageId,
            input));
    }

    private FlowMessage CreateDiagnosticMessage(
        ApplicationPortRejection rejection)
    {
        var error = rejection.Exception is null ? null : CreateFlowError(rejection);
        var diagnostic = new FlowEvent
        {
            Timestamp = rejection.Timestamp,
            Name = ApplicationDiagnosticNames.PortRejected,
            Kind = FlowEventKind.Diagnostic,
            Level = rejection.Reason is ApplicationPortRejectionReason.ConditionFailed or
                ApplicationPortRejectionReason.SourceFaulted or
                ApplicationPortRejectionReason.ComponentFaulted or
                ApplicationPortRejectionReason.OutputCaptureFailed
                    ? FlowEventLevel.Error
                    : FlowEventLevel.Warning,
            Message = $"Port activity was rejected with reason '{rejection.Reason}'.",
            Dimensions = CreateAttributes(
                ("kind", metadataByAddress.TryGetValue(rejection.Port, out var metadata) &&
                    metadata.Direction == ApplicationPortDirection.Input ? "Input" : "Output"),
                ("subject", rejection.Port.Value),
                ("reason", rejection.Reason.ToString())),
            Attributes = CreateAttributes(
                ("port", rejection.Port.Value),
                ("relatedPort", rejection.RelatedPort?.Value),
                ("reason", rejection.Reason.ToString())),
            Details = error is null ? FlowValue.Null : FlowValue.From(error)
        };
        return ApplicationSignalMessage.Create(
            diagnostic,
            rejection.CorrelationId,
            rejection.TraceId,
            rejection.MessageId,
            rejection.Port);
    }

    private static FlowMessage CreateSystemEventMessage(
        ApplicationPortRejection rejection)
    {
        var error = CreateFlowError(rejection);
        var systemEvent = new FlowEvent
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
            Kind = FlowEventKind.Lifecycle,
            Level = FlowEventLevel.Error,
            Message = error.Message,
            Dimensions = CreateAttributes(
                ("category", rejection.Reason is ApplicationPortRejectionReason.SourceFaulted or
                    ApplicationPortRejectionReason.ComponentFaulted ? "Component" : "Link"),
                ("subject", rejection.Port.Value)),
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal) { ["error"] = error },
            Details = FlowValue.From(CreateDetails(
                ("port", rejection.Port.Value),
                ("relatedPort", rejection.RelatedPort?.Value),
                ("reason", rejection.Reason.ToString())))
        };
        return ApplicationSignalMessage.Create(
            systemEvent,
            rejection.CorrelationId,
            rejection.TraceId,
            rejection.MessageId,
            rejection.Port);
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
            rejection.Reason switch
            {
                ApplicationPortRejectionReason.SourceFaulted or
                    ApplicationPortRejectionReason.ComponentFaulted => "component",
                ApplicationPortRejectionReason.OutputCaptureFailed => "output",
                _ => "link"
            },
            isTransient: rejection.Reason != ApplicationPortRejectionReason.ConditionFailed,
            CreateDetails(
                ("port", rejection.Port.Value),
                ("relatedPort", rejection.RelatedPort?.Value)));

    private static Dictionary<string, object?> CreateAttributes(
        (string Name, string? Value) first,
        (string Name, string? Value) second)
    {
        var attributes = new Dictionary<string, object?>(2, StringComparer.Ordinal);
        AddAttribute(attributes, first);
        AddAttribute(attributes, second);
        return attributes;
    }

    private static Dictionary<string, object?> CreateAttributes(
        (string Name, string? Value) first,
        (string Name, string? Value) second,
        (string Name, string? Value) third)
    {
        var attributes = new Dictionary<string, object?>(3, StringComparer.Ordinal);
        AddAttribute(attributes, first);
        AddAttribute(attributes, second);
        AddAttribute(attributes, third);
        return attributes;
    }

    private static JsonElement CreateDetails(
        (string Name, string? Value) first,
        (string Name, string? Value) second)
        => JsonSerializer.SerializeToElement(CreateAttributes(first, second));

    private static JsonElement CreateDetails(
        (string Name, string? Value) first,
        (string Name, string? Value) second,
        (string Name, string? Value) third)
        => JsonSerializer.SerializeToElement(CreateAttributes(first, second, third));

    private static void AddAttribute(
        Dictionary<string, object?> attributes,
        (string Name, string? Value) attribute)
    {
        if (attribute.Value is not null)
            attributes.Add(attribute.Name, attribute.Value);
    }
}
