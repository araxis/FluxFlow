namespace FluxFlow.ComponentPackageTemplate.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
        => utcNow;
}
