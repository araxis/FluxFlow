namespace FluxFlow.Components.Validation.Timing;

public interface IValidationClock
{
    DateTimeOffset UtcNow { get; }
}
