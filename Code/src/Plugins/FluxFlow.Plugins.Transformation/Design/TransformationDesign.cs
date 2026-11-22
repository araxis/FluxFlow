using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.Batching;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Configuration.Components.Reducing;
using FluxFlow.Configuration.Sources;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Plugins.Transformation;

public sealed class TransformationDesign : IPluginDesign
{
    private static readonly ValidationNote Expressions = new("Not validated: expression engine resolution, file access and compilation require the runner.");

    public PropertyRules Component(PluginComponent component) => component.Palette.ComponentType switch
    {
        "mapper" => new() { Validate = s => Read<MapperConfiguration>(s).Validate(), ValidationNote = _ => Expressions },
        "batch" => new() { Validate = s => Read<BatchConfiguration>(s).Validate() },
        "reducer" => new()
        {
            Validate = s =>
            {
                var config = Read<ReducerConfiguration>(s);
                config.Validate();
                if (!JsonSource.IsFileReference(config.InitialState.Value)) config.InitialState.Read(AppContext.BaseDirectory);
            },
            ValidationNote = s => JsonSource.IsFileReference(s["InitialState"])
                ? new("Initial JSON uses an external file. File access belongs to the runner.", IsWarning: true) : Expressions
        },
        _ => throw new ArgumentException("Unknown transformation component.", nameof(component))
    };

    public PropertyRules Connection(PluginConnection connection) => throw new ArgumentException("Transformation has no connections.", nameof(connection));

    private static T Read<T>(IConfiguration settings) where T : class, new() =>
        settings is IConfigurationSection section ? section.ReadRequiredConfiguration<T>()
            : settings.Get<T>() ?? throw new ArgumentException("Missing component settings.");
}
