namespace FluxFlow.ReportingSample;

/// <summary>
/// A report definition owns its discovery metadata and event selection.
/// Register implementations in DI; pass Includes to a standard Dataflow LinkTo predicate.
/// Implementations must be thread-safe and must not perform I/O in Includes.
/// </summary>
public interface IApplicationReport
{
    ApplicationReportDescriptor Descriptor { get; }
    bool Includes(ApplicationReportEvent record);
}
