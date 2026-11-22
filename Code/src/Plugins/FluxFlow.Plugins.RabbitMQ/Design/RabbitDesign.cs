using FluxFlow.Configuration.Components.RabbitMQ;
using FluxFlow.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.RabbitMQ;

public sealed class RabbitDesign : IPluginDesign
{
    public PropertyRules Component(PluginComponent component)
    {
        if (component.Palette.ComponentType == "amqp.settlement")
            return new() { Validate = section => {
                ArgumentException.ThrowIfNullOrWhiteSpace(section["Consumer"]);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(section.GetValue("Capacity", 128));
            } };
        var consumer = component.Palette.ComponentType == "amqp.consumer";
        var rules = new PropertyRules { ObjectGroups = RabbitDeclarationFields.Groups,
            Validate = settings => ValidateSettings(settings, consumer) };
        return consumer ? rules with { TextValidators = new Dictionary<string, Func<string?, string?>>
            { ["Queue"] = value => RabbitDeclarationFields.Validate("Queue", value) } } : rules;
    }
    private static void ValidateSettings(IConfiguration settings, bool consumer)
    {
        if (settings is IConfigurationSection section && section.Path.Split(':') is [.., var parent, _, _] &&
            parent.Equals("Workflows", StringComparison.OrdinalIgnoreCase))
        {
            RabbitComponentConfiguration.Read(section).Validate(consumer);
            return;
        }

        // The runtime resolves relative port links against their owning workflow. A detached draft has no owner.
        const string path = "Workflows:draft:component";
        using var draft = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            settings.AsEnumerable(makePathsRelative: true).Select(pair => KeyValuePair.Create(path + ":" + pair.Key, pair.Value))).Build();
        RabbitComponentConfiguration.Read(draft.GetSection(path)).Validate(consumer);
    }

    public PropertyRules Connection(PluginConnection connection) => RabbitConnectionProperties.Rules;
    public void ConfigureRemoteServices(IServiceCollection services) => services.AddSingleton<IResourceEnvironmentAdapter, RabbitMqEnvironmentAdapter>()
        .AddSingleton(RabbitMqAmqpSetup.Provider);
}
