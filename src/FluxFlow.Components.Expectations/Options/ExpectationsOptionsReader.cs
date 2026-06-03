using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Expectations.Options;

internal static class ExpectationsOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static EventExpectationSettings ReadEventExpectationSettings(NodeDefinition definition)
    {
        var options = Read<EventExpectationOptions>(definition);
        var filter = options.Filter ?? new EventFilter();

        ValidateOptionalText(options.Name, "name");
        ValidateFilter(filter);

        if (options.TimeoutMilliseconds.HasValue && options.TimeoutMilliseconds.Value <= 0)
        {
            throw new InvalidOperationException(
                "event expectation option 'timeoutMilliseconds' must be greater than zero when set.");
        }

        if (options.MaxObservedEvents < 0)
        {
            throw new InvalidOperationException(
                "event expectation option 'maxObservedEvents' must be zero or greater.");
        }

        if (options.MaxPreviewChars < 0)
        {
            throw new InvalidOperationException(
                "event expectation option 'maxPreviewChars' must be zero or greater.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "event expectation option 'boundedCapacity' must be greater than zero.");
        }

        return new EventExpectationSettings
        {
            Name = Normalize(options.Name),
            Filter = filter,
            Timeout = options.TimeoutMilliseconds.HasValue
                ? TimeSpan.FromMilliseconds(options.TimeoutMilliseconds.Value)
                : null,
            MaxObservedEvents = options.MaxObservedEvents,
            MaxPreviewChars = options.MaxPreviewChars,
            BoundedCapacity = options.BoundedCapacity
        };
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }

    private static void ValidateFilter(EventFilter filter)
    {
        ValidateOptionalText(filter.Type, "filter.type");
        ValidateOptionalText(filter.TypePrefix, "filter.typePrefix");
        ValidateOptionalText(filter.SubjectPrefix, "filter.subjectPrefix");
        ValidateOptionalText(filter.ChannelPrefix, "filter.channelPrefix");
        ValidateOptionalText(filter.ExcludedSubjectPrefix, "filter.excludedSubjectPrefix");
        ValidateOptionalText(filter.ExcludedChannelPrefix, "filter.excludedChannelPrefix");
        ValidateOptionalText(filter.Status, "filter.status");
        ValidateOptionalText(filter.Source, "filter.source");
        ValidateOptionalText(filter.SourceNodeId, "filter.sourceNodeId");
        ValidateOptionalText(filter.ComponentId, "filter.componentId");

        if (filter.From.HasValue &&
            filter.To.HasValue &&
            filter.From.Value > filter.To.Value)
        {
            throw new InvalidOperationException(
                "event expectation option 'filter.from' cannot be later than filter.to.");
        }

        if (filter.Attributes is null)
        {
            return;
        }

        foreach (var (key, _) in filter.Attributes)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "event expectation option 'filter.attributes' cannot contain an empty key.");
            }
        }
    }

    private static void ValidateOptionalText(string? value, string optionName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"event expectation option '{optionName}' cannot be empty when set.");
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
