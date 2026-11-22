using FluxFlow.Environments;

namespace FluxFlow.Studio.UI.Features.Observations.Logs;

public sealed record ExecutionLogRow(string Source, ExecutionLog Record)
{
    public DateTime Time => Record.Timestamp.UtcDateTime;
    public int? Severity => Record.Telemetry?.SeverityNumber is >= 1 and <= 24
        ? (Record.Telemetry.SeverityNumber - 1) / 4 : null;
    public string Category => Record.Category;
    public string Message => Record.Message;
}
