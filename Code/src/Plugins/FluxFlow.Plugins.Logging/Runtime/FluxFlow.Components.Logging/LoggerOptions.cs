using Microsoft.Extensions.Logging;

namespace FluxFlow.Components.Logging;

public sealed class LoggerOptions
{
    public required ILogger Logger { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
