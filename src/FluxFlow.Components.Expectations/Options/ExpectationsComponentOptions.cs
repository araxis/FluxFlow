using FluxFlow.Components.Expectations.Timing;

namespace FluxFlow.Components.Expectations.Options;

public sealed class ExpectationsComponentOptions
{
    public IExpectationClock Clock { get; private set; } = SystemExpectationClock.Instance;

    public ExpectationsComponentOptions UseClock(IExpectationClock clock)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }
}
