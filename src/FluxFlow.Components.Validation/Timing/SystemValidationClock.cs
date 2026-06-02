namespace FluxFlow.Components.Validation.Timing;

public sealed class SystemValidationClock : IValidationClock
{
    public static SystemValidationClock Instance { get; } = new();

    private SystemValidationClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
