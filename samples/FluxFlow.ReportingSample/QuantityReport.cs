using FluxFlow.Nodes;

namespace FluxFlow.ReportingSample;

/// <summary>A report added through its own implementation and DI registration only.</summary>
public sealed class QuantityReport(IReportArchive archive) : IApplicationReport
{
    public ApplicationReportDescriptor Descriptor { get; } = new()
    {
        Type = "sample.measurement", Category = "measurements", Description = "Observed sample quantity",
        Fields = Array.AsReadOnly(new[]
        {
            new ApplicationReportFieldDescriptor
            {
                Path = "/measurements/value", Description = "Quantity", Kind = ApplicationReportFieldKind.Number,
                Unit = "items", Measurement = ApplicationReportMeasurementKind.Sample
            }
        })
    };

    public bool Includes(ApplicationReportEvent record)
        => record.SchemaVersion == Descriptor.Version &&
           record.Message.Headers.GetValueOrDefault(FlowEventHeaders.Type) == Descriptor.Type;

    public Task<HistoricalReport> GenerateAsync(Guid captureId, ApplicationReportFilter filter, CancellationToken token = default)
        => archive.QueryAsync(captureId, this, filter, "quantity.v1", token);
}
