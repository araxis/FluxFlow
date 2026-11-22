using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Filtering;
using FluxFlow.Configuration.Components.Routing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Plugins.Routing;

public sealed class RoutingDesign : IPluginDesign
{
    public PropertyRules Component(PluginComponent component)
    {
        var type = component.Palette.ComponentType;
        return new()
        {
            Validate = settings => Validate(type, settings),
            ValidationNote = type == "gate" ? null : _ => new("Not validated: expression engine resolution, file access and compilation require the runner.")
        };
    }

    public PropertyRules Connection(PluginConnection connection) => throw new ArgumentException("Routing has no connections.", nameof(connection));

    private static void Validate(string type, IConfiguration settings)
    {
        if (settings is not IConfigurationSection section)
        {
            const string prefix = "Workflows:draft:component";
            using var draft = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(settings.AsEnumerable(true)
                .Select(item => KeyValuePair.Create(prefix + ":" + item.Key, item.Value))).Build();
            Validate(type, draft.GetSection(prefix));
            return;
        }
        switch (type)
        {
            case "gate": section.ReadRequiredConfiguration<GateConfiguration>().Validate(); break;
            case "changeFilter": section.ReadRequiredConfiguration<ChangeFilterConfiguration>().Validate(); break;
            case "deduplicate": section.ReadRequiredConfiguration<DeduplicateConfiguration>().Validate(); break;
            case "router":
                var config = section.ReadRequiredConfiguration<RouterConfiguration>();
                var address = section.GetComponentAddress();
                config.Validate(address);
                foreach (var name in config.Routes.Keys)
                {
                    var links = section.GetSection(name);
                    if (links.Exists()) OneOrManyPortReferences.Read(links, address);
                }
                break;
            default: throw new ArgumentException("Unknown routing component.", nameof(type));
        }
    }
}
