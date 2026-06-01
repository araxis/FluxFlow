namespace FluxFlow.ComponentPackageTemplate.Options;

public sealed class TemplateComponentOptions
{
    public TimeProvider TimeProvider { get; private set; } = TimeProvider.System;

    public TemplateComponentOptions UseTimeProvider(TimeProvider timeProvider)
    {
        TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        return this;
    }
}
