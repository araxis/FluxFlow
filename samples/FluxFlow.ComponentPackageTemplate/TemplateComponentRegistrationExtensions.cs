using FluxFlow.ComponentPackageTemplate.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.ComponentPackageTemplate;

public static class TemplateComponentRegistrationExtensions
{
    public static RuntimeNodeFactoryRegistry RegisterTemplateComponents(
        this RuntimeNodeFactoryRegistry registry)
        => registry.RegisterTemplateComponents(_ => { });

    public static RuntimeNodeFactoryRegistry RegisterTemplateComponents(
        this RuntimeNodeFactoryRegistry registry,
        Action<TemplateComponentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TemplateComponentOptions();
        configure(options);
        return registry.Register(new TemplateComponentModule(options));
    }
}
