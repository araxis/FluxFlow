using System.Text.Json;
using FluxFlow.Configuration.Sources;
using Cronos;
using FluxFlow.Authoring.Definitions.DataGenerator;
using FluxFlow.Authoring.Definitions.Scheduler;
using FluxFlow.Configuration.Components;
using FluxFlow.Configuration.Components.DataGeneration;
using FluxFlow.Configuration.Components.Scheduling;
using FluxFlow.Configuration.Components.Sources;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Plugins.Sources;

public sealed class SourcesDesign : IPluginDesign
{
    public PropertyRules Component(PluginComponent component) => component.Palette.ComponentType switch
    {
        "scheduler" => new() { Inspector = new SchedulerDescription().GetInspector, Groups = SchedulerDescription.Groups, Validate = ValidateScheduler },
        "source" => new()
        {
            Validate = ValidateSource,
            ValidationNote = settings => JsonSource.IsFileReference(settings["Values"])
                ? new("Initial JSON uses an external file. File access belongs to the runner.", IsWarning: true) : null
        },
        "dataGenerator" => new()
        {
            Validate = ValidateGenerator,
            TextValidators = new DataGeneratorDescription().Inspector.Sections.SelectMany(s => s.Options)
                .Where(o => o.ValidateText is not null).ToDictionary(o => o.Name, o => o.ValidateText!)
        },
        _ => throw new ArgumentException("Unknown Sources component.", nameof(component))
    };
    public PropertyRules Connection(PluginConnection connection) => throw new ArgumentException("Sources has no connections.", nameof(connection));

    private static T Read<T>(IConfiguration settings) where T : class, new() =>
        settings is IConfigurationSection section ? section.ReadRequiredConfiguration<T>()
            : settings.Get<T>() ?? throw new ArgumentException("Missing component settings.");

    private static void ValidateScheduler(IConfiguration settings)
    {
        var config = Read<SchedulerConfiguration>(settings);
        if (settings.GetSection("Input").Exists()) throw new ArgumentException("Scheduler has no Input port.");
        config.Validate();
        if (config.Cron is { } cron) CronExpression.Parse(cron, config.IncludeSeconds ? CronFormat.IncludeSeconds : CronFormat.Standard);
    }

    private static void ValidateSource(IConfiguration settings)
    {
        var config = Read<SourceConfiguration>(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(config.OutputCapacity);
        ArgumentNullException.ThrowIfNull(config.Values);
        // The design host must never open a runner file.
        if (JsonSource.IsFileReference(config.Values.Value)) return;
        if (config.Values.Read(AppContext.BaseDirectory).ValueKind != JsonValueKind.Array) throw new ArgumentException("Values must contain a JSON array.");
    }

    private static void ValidateGenerator(IConfiguration settings)
    {
        var config = Read<DataGeneratorConfiguration>(settings);
        config.Validate();
    }
}
