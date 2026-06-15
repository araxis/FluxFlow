namespace FluxFlow.Components.Expectations.Options;

public sealed class ExpectationsComponentOptions
{
    public TimeProvider Clock { get; private set; } = TimeProvider.System;

    public ExpectationsComponentOptions UseClock(TimeProvider clock)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }
}
