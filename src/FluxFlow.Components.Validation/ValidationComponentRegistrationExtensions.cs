using FluxFlow.Components.Validation.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Validation;

public static class ValidationComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterValidationComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterValidationComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterValidationComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<ValidationComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ValidationComponentOptions();
        configure(options);
        return registry.Register(new ValidationComponentModule(options));
    }
}
