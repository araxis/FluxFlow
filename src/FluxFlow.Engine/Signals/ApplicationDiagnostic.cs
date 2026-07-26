using System.Collections.Immutable;
using FluxFlow.Data;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Engine.Signals;

public enum ApplicationDiagnosticKind
{
    Runtime = 1,
    Input = 2,
    Output = 3,
    Timing = 4,
    Log = 5,
    Metric = 6,
    Trace = 7
}

public enum ApplicationDiagnosticLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

public static class ApplicationDiagnosticNames
{
    public const string InputAccepted = "flow.port.input.accepted";
    public const string OutputEmitted = "flow.port.output.emitted";
    public const string PortRejected = "flow.port.rejected";
    public const string RequestCompleted = "flow.port.request.completed";
    public const string SystemEventDeliveryFailed = "flow.system.event.delivery.failed";
}

public sealed record ApplicationDiagnostic
{
    private IReadOnlyDictionary<string, string> _attributes =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public required DateTimeOffset Timestamp { get; init; }

    public required string Name { get; init; }

    public required ApplicationDiagnosticKind Kind { get; init; }

    public ApplicationDiagnosticLevel Level { get; init; } = ApplicationDiagnosticLevel.Information;

    public string? Subject { get; init; }

    public string? Message { get; init; }

    public TimeSpan? Duration { get; init; }

    public double? Measurement { get; init; }

    public string? Unit { get; init; }

    public DataFlowError? Error { get; init; }

    public IReadOnlyDictionary<string, string> Attributes
    {
        get => _attributes;
        init
        {
            if (value is null || value.Count == 0)
            {
                _attributes = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);
                return;
            }

            var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var attribute in value)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(attribute.Key);
                builder.Add(
                    attribute.Key,
                    attribute.Value ?? throw new ArgumentException(
                        "Diagnostic attributes cannot contain null values.",
                        nameof(value)));
            }

            _attributes = builder.ToImmutable();
        }
    }
}
