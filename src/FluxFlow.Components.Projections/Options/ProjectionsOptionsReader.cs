using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Projections.Options;

internal static class ProjectionsOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static EventProjectionOptions ReadEventProjectionOptions(NodeDefinition definition)
    {
        var options = Read<EventProjectionOptions>(definition);
        options = options with { Filter = options.Filter ?? new EventFilter() };

        if (options.RateWindowSeconds <= 0)
        {
            throw new InvalidOperationException(
                "event.projection option 'rateWindowSeconds' must be greater than zero.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "event.projection option 'boundedCapacity' must be greater than zero.");
        }

        if (options.MaxPreviewChars < 0)
        {
            throw new InvalidOperationException(
                "event.projection option 'maxPreviewChars' must be zero or greater.");
        }

        ValidateOptionalText(options.Name, "name");
        ValidateFilter(options.Filter);
        return options;
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
        ArgumentNullException.ThrowIfNull(filter);

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
                "event.projection option 'filter.from' cannot be later than filter.to.");
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
                    "event.projection option 'filter.attributes' cannot contain an empty key.");
            }
        }
    }

    private static void ValidateOptionalText(string? value, string optionName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"event.projection option '{optionName}' cannot be empty when set.");
        }
    }
}
