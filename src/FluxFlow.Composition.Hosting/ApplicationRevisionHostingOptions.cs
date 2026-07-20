namespace FluxFlow.Composition.Hosting;

public sealed class ApplicationRevisionHostingOptions
{
    public string InitialRevisionId { get; set; } = "initial";

    public bool StartApplicationWithHost { get; set; } = true;

    public bool StopApplicationWithHost { get; set; } = true;
}
