using FluxFlow.Engine.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerComponentModuleTests
{
    [Fact]
    public void RegisterTimerComponents_RegistersIntervalNode()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterTimerComponents();

        registry.TryGetFactory(TimerComponentTypes.Debounce, out _).ShouldBeTrue();
        registry.TryGetFactory(TimerComponentTypes.Delay, out _).ShouldBeTrue();
        registry.TryGetFactory(TimerComponentTypes.Interval, out _).ShouldBeTrue();
        registry.TryGetFactory(TimerComponentTypes.Schedule, out _).ShouldBeTrue();
        registry.TryGetFactory(TimerComponentTypes.Throttle, out _).ShouldBeTrue();
    }
}
