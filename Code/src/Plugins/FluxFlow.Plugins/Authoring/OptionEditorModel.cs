using System.Globalization;

namespace FluxFlow.Authoring;

/// <summary>
/// A component property and its shared editor, label, help text and optional constraints.
/// </summary>
public sealed record OptionEditorModel
{
    public static OptionEditorModel Create(string name, string displayName, OptionEditorKind editor = OptionEditorKind.Text,
        string? help = null, object? defaultValue = null) =>
        new() { Name = name, DisplayName = displayName, Editor = editor, HelperText = help, DefaultValue = defaultValue };

    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required OptionEditorKind Editor { get; init; }
    public string? HelperText { get; init; }
    public string? Syntax { get; init; }
    public bool PreviewExpression { get; init; } = true;
    public string? PreviewProvider { get; init; }
    /// <summary>Initial JSON sample for a custom value-only context. Null retains the message workspace.</summary>
    public string? PreviewSample { get; init; }
    public IReadOnlyList<string> PreviewFields { get; init; } = [];
    public ExpressionResultKind ExpressionResult { get; init; }
    public bool JsonArrayOnly { get; init; }
    public bool AllowEmptyText { get; init; }
    public bool IsRequired { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<string?, string?>? ValidateText { get; init; }
    public bool IsAdvanced { get; init; }
    public object? DefaultValue { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    /// <summary>Subtract the current value of this sibling integer field from Max, if it meets its minimum.</summary>
    public string? MaxMinusField { get; init; }
    public TimeSpan? MinDuration { get; init; }
    public TimeSpan? MaxDuration { get; init; }
    public bool AllowInfiniteDuration { get; init; }
    public IReadOnlyList<OptionChoiceModel> Choices { get; init; } = [];
    public bool PreserveChoiceSpelling { get; init; }
    public bool IgnoreChoiceCase { get; init; }
    public OptionVisibility? VisibleWhen { get; init; }
    public FluxFlow.Plugins.SuggestionSource? Suggestions { get; init; }
    public string? RelatedResource { get; init; }
    public string? RelatedComponent { get; init; }
}

// Unknown controlling values leave dependent fields available for repair.
public sealed record OptionVisibility(string Field, string Choice)
{
    public bool MatchUnknown { get; init; } = true;

    public bool Matches(IReadOnlyList<OptionEditorModel> options, IReadOnlyDictionary<string, System.Text.Json.JsonElement> settings)
    {
        var source = options.FirstOrDefault(option => option.Name.Equals(Field, StringComparison.OrdinalIgnoreCase));
        if (source is null) return MatchUnknown;
        var raw = settings.FirstOrDefault(pair => pair.Key.Equals(Field, StringComparison.OrdinalIgnoreCase)).Value;
        var value = raw.ValueKind == System.Text.Json.JsonValueKind.Undefined ? source.DefaultValue?.ToString() : raw.ToString();
        var selected = source.Choices.FirstOrDefault(option => option.Matches(value, ignoreCase: true));
        return selected is null ? MatchUnknown : selected.Value == Choice;
    }
}

public sealed record OptionChoiceModel
{
    public static OptionChoiceModel FromEnum<T>(T value, string? label = null) where T : struct, Enum =>
        new() { Value = value.ToString(), DisplayName = label ?? value.ToString(), NumericValue = value.ToString("D") };

    public bool Matches(string? saved, bool ignoreCase)
    {
        if (NumericValue is null)
            return string.Equals(Value, saved, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        // Enum settings can be saved as names or numbers, including by older configuration editors.
        var text = saved?.Trim();
        return string.Equals(Value, text, StringComparison.OrdinalIgnoreCase) || NumericValue == text ||
            (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
             && number.ToString(CultureInfo.InvariantCulture) == NumericValue);
    }

    public string? NumericValue { get; init; }
    public required string Value { get; init; }
    public required string DisplayName { get; init; }
    public string? HelperText { get; init; }
}

/// <summary>
/// The shared property editors available to component descriptions.
/// </summary>
public enum OptionEditorKind
{
    Text,
    MultilineText,
    Number,
    Toggle,
    Select,
    Duration,
    Secret,
    Expression,
    Json,
    JsonSource,
    DateTimeOffset,
    Integer,
    Base64
}
