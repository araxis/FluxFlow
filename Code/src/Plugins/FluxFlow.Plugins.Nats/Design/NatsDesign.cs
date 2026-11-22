using System.Text.Json;
using FluxFlow.Authoring.Definitions.JetStream;
using FluxFlow.Authoring.Definitions.Nats;
using FluxFlow.Components.Nats;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Nats;
using FluxFlow.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins.Nats;

public sealed class NatsDesign : IPluginDesign
{
    public PropertyRules Component(PluginComponent component)
    {
        var type = component.Palette.ComponentType;
        var fields = type.StartsWith("nats.jetstream.", StringComparison.Ordinal) ? JetStreamDescription.Options(type) : NatsDescription.Options(type);
        return new() { Validate = settings => Validate(type, settings), IsVisible = Visible, TextValidators = fields.Where(f => f.ValidateText is not null)
            .ToDictionary(f => f.Name, f => f.ValidateText!) };
    }
    public PropertyRules Connection(PluginConnection connection) => NatsConnectionProperties.Rules;
    public void ConfigureRemoteServices(IServiceCollection services) => services.AddSingleton<IResourceEnvironmentAdapter, NatsEnvironmentAdapter>()
        .AddSingleton(NatsSetup.Provider);

    private static bool? Enabled(IReadOnlyDictionary<string, JsonElement> configuration, string name)
    {
        var value = configuration.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        if (value.ValueKind == JsonValueKind.Undefined) return false;
        return bool.TryParse(value.ToString(), out var enabled) ? enabled : null;
    }
    private static bool Visible(IReadOnlyDictionary<string, JsonElement> configuration, string name) => name switch
    {
        "StreamSubject" or "Storage" or "MaxStreamMessages" or "MaxStreamBytes" or "MaxStreamAgeSeconds" => Enabled(configuration, "CreateStream") != false,
        "FilterSubject" or "DeliverPolicy" or "AckWaitSeconds" or "MaxDeliver" => Enabled(configuration, "CreateConsumer") != false,
        _ => true
    };
    private static void Validate(string type, IConfiguration settings)
    {
        if (settings is IConfigurationSection section && section.Path.Split(':') is [.., var parent, _, _] &&
            parent.Equals("Workflows", StringComparison.OrdinalIgnoreCase))
        {
            ValidateComponent(type, section);
            return;
        }

        // Give relative links in a standalone editor draft the same context as saved workflow links.
        const string path = "Workflows:draft:component";
        using var draft = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            settings.AsEnumerable(makePathsRelative: true).Select(pair => KeyValuePair.Create(path + ":" + pair.Key, pair.Value))).Build();
        ValidateComponent(type, draft.GetSection(path));
    }

    private static void ValidateComponent(string type, IConfigurationSection section)
    {
        switch (type)
        {
            case "nats.jetstream.settlement":
                section.ReadRequiredConfiguration<JetStreamSettlementConfiguration>().Validate();
                break;
            case "nats.jetstream.consumer":
                section.ReadRequiredConfiguration<JetStreamConfiguration>().ConsumerOptions();
                break;
            case "nats.jetstream.publisher":
                section.ReadRequiredConfiguration<JetStreamConfiguration>().StreamOptions();
                break;
            case "nats.subscriber":
                var subscriber = section.ReadRequiredConfiguration<NatsSubscriberConfiguration>();
                subscriber.Options();
                NatsComponentOptions.ValidateSubject(subscriber.Subject, true);
                if (subscriber.QueueGroup is not null) NatsComponentOptions.ValidateSubject(subscriber.QueueGroup);
                break;
            default:
                section.ReadRequiredConfiguration<NatsCommandConfiguration>().Options();
                break;
        }
    }
}
