namespace FluxFlow.Composition.Hosting;

public sealed class CompositionHostingOptions
{
    public bool StartRuntimeWithHost { get; set; } = true;

    public bool StopRuntimeWithHost { get; set; } = true;

    public bool ThrowOnBuildFailure { get; set; } = true;

    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
