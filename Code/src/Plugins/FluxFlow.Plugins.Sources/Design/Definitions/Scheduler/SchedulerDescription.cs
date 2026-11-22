using FluxFlow.Configuration.Components.Scheduling;
using Cronos;
using FluxFlow.Authoring;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Plugins;

using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Scheduler;

public sealed class SchedulerDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "scheduler", DisplayName = "Scheduler", Category = "Sources",
        PreferredNodeName = "scheduler", Icon = """<path d="M0 0h24v24H0V0z" fill="none"/><path d="M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z"/>""",
        DefaultColor = "#bc94e6",
        Summary = "Produces signals according to its configured schedule.",
        Inputs = [],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "scheduler",
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(SchedulerConfiguration.Interval), "Interval", OptionEditorKind.Duration,
                    "Time between signals, for example 00:00:01 for one second. Set exactly one of Interval, Cron or At.") with { MinDuration = TimeSpan.FromTicks(1) },
                Create(nameof(SchedulerConfiguration.At), "At", OptionEditorKind.DateTimeOffset,
                    help: "Date and time for a single signal, including the UTC offset. Set exactly one of Interval, Cron or At."),
                Create(nameof(SchedulerConfiguration.Cron), "Cron",
                    help: "Recurring cron schedule: five fields, or six with Include seconds. Set exactly one of Interval, Cron or At.") with { ValidateText = value => ValidateCron(value, false) },
                Create(nameof(SchedulerConfiguration.IncludeSeconds), "Include seconds", OptionEditorKind.Toggle,
                    "Use a seconds field in the cron expression. Applies only to Cron.", defaultValue: false),
                Create(nameof(SchedulerConfiguration.TimeZone), "Time zone",
                    help: "Time zone identifier on the runner for a cron schedule. When omitted, UTC is used. Applies only to Cron."),
                Create(nameof(SchedulerConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum number of messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };

    public NodeInspectorModel GetInspector(IReadOnlyDictionary<string, JsonElement> configuration)
    {
        var seconds = configuration.FirstOrDefault(item => item.Key.Equals(nameof(SchedulerConfiguration.IncludeSeconds), StringComparison.OrdinalIgnoreCase)).Value;
        var includeSeconds = bool.TryParse(seconds.ToString(), out var enabled) && enabled;
        var schedules = ScheduleNames.Where(name => HasValue(configuration, name)).ToArray();
        var selected = schedules.Length == 1 ? schedules[0] : null;
        // Conflicting or incomplete saved schedules stay visible until explicitly repaired.
        var showTimeZone = configuration.Any(item =>
            (item.Key.Equals(nameof(SchedulerConfiguration.Cron), StringComparison.OrdinalIgnoreCase) ||
             item.Key.Equals(nameof(SchedulerConfiguration.TimeZone), StringComparison.OrdinalIgnoreCase) ||
             item.Key.Equals(nameof(SchedulerConfiguration.IncludeSeconds), StringComparison.OrdinalIgnoreCase)) &&
            item.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined));
        return Inspector with { Sections = Inspector.Sections.Select(section => section with
        {
            Options = section.Options.Where(option =>
                    (!ScheduleNames.Contains(option.Name) || schedules.Length > 1 || option.Name == selected ||
                        (schedules.Length == 0 && showTimeZone && option.Name == "Cron")) &&
                    (option.Name is not (nameof(SchedulerConfiguration.TimeZone) or nameof(SchedulerConfiguration.IncludeSeconds)) || showTimeZone))
                .Select(option => option.Name == nameof(SchedulerConfiguration.Cron)
                    ? option with { ValidateText = value => ValidateCron(value, includeSeconds) } : option).ToArray()
        }).ToArray() };
    }

    private static readonly string[] ScheduleNames = ["Interval", "Cron", "At"];
    private static bool HasValue(IReadOnlyDictionary<string, JsonElement> settings, string name) =>
        PropertyGroup.Field(settings, name).ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    public static IReadOnlyList<PropertyGroup> Groups(JsonObject settings)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings.ToJsonString())!;
        return new SchedulerDescription().GetInspector(values).Sections.Select(section =>
            new PropertyGroup(section.Name, null, section.Options) { Fields = ScheduleFields }).ToArray();
    }

    private static IReadOnlyList<ProjectedField> ScheduleFields(IReadOnlyDictionary<string, JsonElement> settings)
    {
        var schedules = ScheduleNames.Where(name => HasValue(settings, name)).ToArray();
        return [new("Schedule", schedules.Length == 1 ? schedules[0] : "")
        {
            Choices = [.. schedules.Length == 1 ? Array.Empty<OptionChoiceModel>() : [new() { Value = "", DisplayName = schedules.Length > 1 ? "Conflicting settings" : "Choose schedule" }],
                new() { Value = "Interval", DisplayName = "Interval" }, new() { Value = "Cron", DisplayName = "Cron" },
                new() { Value = "At", DisplayName = "Once" }],
            Hint = "Choose a schedule. Switching replaces the other schedule fields; Undo restores them. Once requires a date and UTC offset.",
            Write = ChangeSchedule
        }];
    }

    private static void ChangeSchedule(IDictionary<string, JsonElement> settings, string schedule)
    {
        if (!ScheduleNames.Contains(schedule)) return;
        var values = (IReadOnlyDictionary<string, JsonElement>)settings;
        var existing = settings.Keys.FirstOrDefault(key => key.Equals(schedule, StringComparison.OrdinalIgnoreCase)) ?? schedule;
        var hasValue = HasValue(values, schedule);
        foreach (var key in settings.Keys.Where(key =>
            ScheduleNames.Any(name => name.Equals(key, StringComparison.OrdinalIgnoreCase) && name != schedule) ||
            (schedule != "Cron" && (key.Equals("TimeZone", StringComparison.OrdinalIgnoreCase) || key.Equals("IncludeSeconds", StringComparison.OrdinalIgnoreCase)))).ToArray())
            settings.Remove(key);
        if (!hasValue)
            settings[existing] = JsonSerializer.SerializeToElement(schedule switch
            {
                "Interval" => "00:00:01",
                "Cron" => bool.TryParse(PropertyGroup.Field(values, "IncludeSeconds").ToString(), out var seconds) && seconds ? "* * * * * *" : "* * * * *",
                _ => ""
            });
    }

    private static string? ValidateCron(string? value, bool includeSeconds)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try { _ = CronExpression.Parse(value, includeSeconds ? CronFormat.IncludeSeconds : CronFormat.Standard); return null; }
        catch (CronFormatException)
        {
            return includeSeconds
                ? "Enter a valid six-field cron expression: seconds, minutes, hours, day, month, day of week."
                : "Enter a valid five-field cron expression: minutes, hours, day, month, day of week.";
        }
    }
}
