namespace FluxFlow.Configuration.Components.Logging;

public sealed record LoggerConfiguration : ComponentConfiguration
{
    /// <summary>Optional logging resource. Omit to use the application's ILoggerFactory.</summary>
    public string? Logger { get; init; }
    public string? Category { get; init; }
    public int OutputCapacity { get; init; } = 128;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputCapacity);
        if (Logger is not null) ArgumentException.ThrowIfNullOrWhiteSpace(Logger);
        if (Category is not null) ArgumentException.ThrowIfNullOrWhiteSpace(Category);
    }
}
