using System.Text.Json;
using System.Text;
using FluxFlow.Authoring;
using Microsoft.Extensions.Configuration;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Plugins.Ros2;

internal static class RosQos
{
    public static void ValidateJson(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Enter a QoS object or clear it to use the runner defaults.");
        using var settings = (ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(value.GetRawText()))).Build();
        _ = settings.Get<QosSettings>(options => options.ErrorOnUnknownConfiguration = true);
    }
    public static Dictionary<string, JsonElement> Defaults()
    {
        var profile = QosSettings.Default;
        return new()
        {
            [nameof(QosSettings.History)] = JsonSerializer.SerializeToElement(profile.History.ToString()),
            [nameof(QosSettings.Depth)] = JsonSerializer.SerializeToElement(profile.Depth),
            [nameof(QosSettings.Reliability)] = JsonSerializer.SerializeToElement(profile.Reliability.ToString()),
            [nameof(QosSettings.Durability)] = JsonSerializer.SerializeToElement(profile.Durability.ToString()),
            [nameof(QosSettings.Deadline)] = JsonSerializer.SerializeToElement(profile.Deadline),
            [nameof(QosSettings.LifeSpan)] = JsonSerializer.SerializeToElement(profile.LifeSpan),
            [nameof(QosSettings.Liveliness)] = JsonSerializer.SerializeToElement(profile.Liveliness.ToString()),
            [nameof(QosSettings.LivelinessLeaseDuration)] = JsonSerializer.SerializeToElement(profile.LivelinessLeaseDuration),
            [nameof(QosSettings.AvoidRosNamespaceConventions)] = JsonSerializer.SerializeToElement(profile.AvoidRosNamespaceConventions)
        };
    }

    public static IReadOnlyList<OptionSectionModel> Sections(IReadOnlyDictionary<string, JsonElement> configuration, bool inherited)
    {
        var defaults = inherited ? QosSettings.Default : new QosSettings();
        var history = configuration.FirstOrDefault(item => item.Key.Equals(nameof(QosSettings.History), StringComparison.OrdinalIgnoreCase)).Value;
        var keepAll = history.ValueKind is JsonValueKind.Number or JsonValueKind.String &&
            Enum.TryParse<HistoryPolicy>(history.ToString(), true, out var policy) && policy == HistoryPolicy.KeepAll;
        var section = new OptionSectionModel
        {
            Name = "QoS",
            Options =
            [
                Policy(nameof(QosSettings.History), "History", defaults.History, configuration),
                Create(nameof(QosSettings.Depth), "Depth", OptionEditorKind.Integer,
                    "Maximum retained samples for Keep last history.", defaultValue: defaults.Depth) with { Min = 0, Max = int.MaxValue },
                Policy(nameof(QosSettings.Reliability), "Reliability", defaults.Reliability, configuration),
                Policy(nameof(QosSettings.Durability), "Durability", defaults.Durability, configuration),
                Create(nameof(QosSettings.Deadline), "Deadline", OptionEditorKind.Duration,
                    "Expected message period as a TimeSpan (for example 00:00:01).", defaultValue: defaults.Deadline.ToString()),
                Create(nameof(QosSettings.LifeSpan), "Lifespan", OptionEditorKind.Duration,
                    "Maximum sample age as a TimeSpan (for example 00:00:05).", defaultValue: defaults.LifeSpan.ToString()),
                Policy(nameof(QosSettings.Liveliness), "Liveliness", defaults.Liveliness, configuration),
                Create(nameof(QosSettings.LivelinessLeaseDuration), "Lease duration", OptionEditorKind.Duration,
                    "Liveliness lease as a TimeSpan (for example 00:00:05).", defaultValue: defaults.LivelinessLeaseDuration.ToString()),
                Create(nameof(QosSettings.AvoidRosNamespaceConventions), "Native names", OptionEditorKind.Toggle,
                    "Bypass ROS namespace conventions when connecting to native DDS topics.", defaultValue: defaults.AvoidRosNamespaceConventions)
            ]
        };
        return [section with { Options = section.Options.Where(option => !keepAll || option.Name != nameof(QosSettings.Depth)).ToArray() }];
    }

    private static OptionEditorModel Policy<T>(string name, string label, T defaultValue, IReadOnlyDictionary<string, JsonElement> configuration) where T : struct, Enum
    {
        var choices = Enum.GetValues<T>().Select(value => new OptionChoiceModel
        {
            Value = value.ToString(), DisplayName = value.ToString()
        }).ToList();
        var saved = configuration.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        if (saved.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            var value = saved.ToString();
            if (choices.All(choice => choice.Value != value))
                choices.Add(new() { Value = value, DisplayName = Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed) ? parsed.ToString() : value });
        }
        return Create(name, label, OptionEditorKind.Select, defaultValue: defaultValue.ToString()) with { Choices = choices };
    }
}
