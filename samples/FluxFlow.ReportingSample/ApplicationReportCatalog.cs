using System.Collections.ObjectModel;

namespace FluxFlow.ReportingSample;

public enum ApplicationReportFieldKind { String, Number, Boolean, Timestamp, Object }
public enum ApplicationReportMeasurementKind { None, Sample, Gauge, Delta, Cumulative }

public sealed record ApplicationReportFieldDescriptor
{
    public required string Path { get; init; }
    public required string Description { get; init; }
    public ApplicationReportFieldKind Kind { get; init; }
    public bool Optional { get; init; } = true;
    public string? Unit { get; init; }
    public ApplicationReportMeasurementKind Measurement { get; init; }
    /// <summary>Required for cumulative counters; identifies when the counter restarts.</summary>
    public string? ResetScope { get; init; }
}

/// <summary>Optional discovery metadata, not validation of every possible dynamic event.</summary>
public sealed record ApplicationReportDescriptor
{
    public required string Type { get; init; }
    public string Category { get; init; } = "general";
    public int Version { get; init; } = 1;
    public required string Description { get; init; }
    public IReadOnlyList<ApplicationReportFieldDescriptor> Fields { get; init; } = [];
}

public sealed class ApplicationReportCatalog
{
    public ApplicationReportCatalog(IEnumerable<IApplicationReport>? reports = null)
    {
        var implementations = (reports ?? []).Take(513).ToArray();
        if (implementations.Length > 512) throw new ArgumentException("At most 512 reports are supported.");
        Reports = Array.AsReadOnly(implementations);
        var keys = new HashSet<(string, int)>();
        var frozen = new List<ApplicationReportDescriptor>();
        foreach (var report in implementations)
        {
            ArgumentNullException.ThrowIfNull(report);
            var descriptor = report.Descriptor;
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Type);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Category);
            ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Description);
            if (descriptor.Version <= 0 || !keys.Add((descriptor.Type, descriptor.Version)))
                throw new ArgumentException("Descriptor type/version must be positive and unique.");
            ArgumentNullException.ThrowIfNull(descriptor.Fields);
            if (descriptor.Fields.Count > 64) throw new ArgumentException("At most 64 fields per event are supported.");
            var fields = new List<ApplicationReportFieldDescriptor>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in descriptor.Fields)
            {
                ArgumentNullException.ThrowIfNull(field);
                ArgumentException.ThrowIfNullOrWhiteSpace(field.Description);
                if (!paths.Add(field.Path) || !Enum.IsDefined(field.Kind) || !Enum.IsDefined(field.Measurement))
                    throw new ArgumentException("Descriptor field paths must be unique and kinds must be defined.");
                new ApplicationReportFilter
                {
                    Conditions = [new ApplicationReportCondition { Path = field.Path, Comparison = ApplicationReportComparison.Exists }]
                }.Compile();
                if (field.Measurement != ApplicationReportMeasurementKind.None && field.Kind != ApplicationReportFieldKind.Number)
                    throw new ArgumentException("Measurements must describe numeric fields.");
                if (field.Measurement == ApplicationReportMeasurementKind.Cumulative && string.IsNullOrWhiteSpace(field.ResetScope))
                    throw new ArgumentException("Cumulative measurements require a reset scope.");
                fields.Add(field with { });
            }
            frozen.Add(descriptor with { Fields = new ReadOnlyCollection<ApplicationReportFieldDescriptor>(fields) });
        }
        Descriptors = frozen.OrderBy(d => d.Type, StringComparer.Ordinal).ThenBy(d => d.Version).ToList().AsReadOnly();
    }

    public IReadOnlyList<ApplicationReportDescriptor> Descriptors { get; }
    public IReadOnlyList<IApplicationReport> Reports { get; }

    /// <summary>Stable fields shared across descriptors; optional source fields may be absent on runtime-level events.</summary>
    public IReadOnlyList<ApplicationReportFieldDescriptor> CommonFields { get; } = Array.AsReadOnly(new[]
    {
        Field("/type", "Event type"), Field("/kind", "Event kind"), Field("/level", "Event level"),
        Field("/timestamp", "Event time", ApplicationReportFieldKind.Timestamp),
        Field("@application", "Host application identity"), Field("@revision", "Producing revision"),
        Field("@schemaVersion", "Event schema version", ApplicationReportFieldKind.Number),
        Field("@phase", "Consumer phase classification"), Field("@source", "Event source"),
        Field("@workflow", "Workflow"), Field("@component", "Component"), Field("@traceId", "Trace identity")
    });

    private static ApplicationReportFieldDescriptor Field(string path, string description,
        ApplicationReportFieldKind kind = ApplicationReportFieldKind.String)
        => new() { Path = path, Description = description, Kind = kind };

    public IReadOnlyList<ApplicationReportComparison> GetComparisons(ApplicationReportFieldKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var common = new[] { ApplicationReportComparison.Exists, ApplicationReportComparison.Missing, ApplicationReportComparison.IsNull };
        return Array.AsReadOnly(kind switch
        {
            ApplicationReportFieldKind.Number => [.. common, ApplicationReportComparison.Equal, ApplicationReportComparison.NotEqual,
                ApplicationReportComparison.GreaterThan, ApplicationReportComparison.GreaterThanOrEqual,
                ApplicationReportComparison.LessThan, ApplicationReportComparison.LessThanOrEqual],
            ApplicationReportFieldKind.String or ApplicationReportFieldKind.Timestamp => [.. common,
                ApplicationReportComparison.Equal, ApplicationReportComparison.NotEqual, ApplicationReportComparison.Contains, ApplicationReportComparison.StartsWith],
            ApplicationReportFieldKind.Boolean => [.. common, ApplicationReportComparison.Equal, ApplicationReportComparison.NotEqual],
            _ => common
        });
    }
}
