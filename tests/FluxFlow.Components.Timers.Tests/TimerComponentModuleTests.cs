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

        registry.TryGetFactory(TimerComponentTypes.Interval, out _).ShouldBeTrue();
    }
}
