namespace FluxFlow.ReportingSample;

/// <summary>The read boundary needed by reports; persistence and capture are separate responsibilities.</summary>
public interface IReportArchive
{
    Task<HistoricalReport> QueryAsync(Guid captureId, IApplicationReport report, ApplicationReportFilter filter,
        string filterVersion, CancellationToken cancellationToken = default);
}
