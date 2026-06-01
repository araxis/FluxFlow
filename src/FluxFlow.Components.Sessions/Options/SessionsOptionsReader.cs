using FluxFlow.Components.Sessions.Contracts;
using FluxFlow.Engine.Definitions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Sessions.Options;

internal static class SessionsOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static SessionRecorderOptions ReadRecorderOptions(NodeDefinition definition)
    {
        var options = Read<SessionRecorderOptions>(definition);
        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "session.recorder option 'boundedCapacity' must be greater than zero.");
        }

        ValidateOptionalText(options.Store, "store", "session.recorder");
        ValidateOptionalText(options.SessionId, "sessionId", "session.recorder");
        ValidateTags(options.Tags, "session.recorder");
        return options;
    }

    public static SessionReplayOptions ReadReplayOptions(NodeDefinition definition)
    {
        var options = Read<SessionReplayOptions>(definition);
        if (string.IsNullOrWhiteSpace(options.SessionId))
        {
            throw new InvalidOperationException(
                "session.replay option 'sessionId' is required.");
        }

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "session.replay option 'boundedCapacity' must be greater than zero.");
        }

        if (options.StartSequence.HasValue && options.StartSequence.Value <= 0)
        {
            throw new InvalidOperationException(
                "session.replay option 'startSequence' must be greater than zero when set.");
        }

        if (options.MaxMessages.HasValue && options.MaxMessages.Value <= 0)
        {
            throw new InvalidOperationException(
                "session.replay option 'maxMessages' must be greater than zero when set.");
        }

        if (options.FixedIntervalMilliseconds < 0)
        {
            throw new InvalidOperationException(
                "session.replay option 'fixedIntervalMilliseconds' must be zero or greater.");
        }

        if (options.SpeedMultiplier <= 0)
        {
            throw new InvalidOperationException(
                "session.replay option 'speedMultiplier' must be greater than zero.");
        }

        ValidateOptionalText(options.Store, "store", "session.replay");
        return options with
        {
            SessionId = options.SessionId.Trim()
        };
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }

    private static void ValidateOptionalText(
        string? value,
        string optionName,
        string nodeType)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{nodeType} option '{optionName}' cannot be empty when set.");
        }
    }

    private static void ValidateTags(
        Dictionary<string, string>? tags,
        string nodeType)
    {
        if (tags is null)
        {
            return;
        }

        foreach (var (key, _) in tags)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    $"{nodeType} option 'tags' cannot contain an empty key.");
            }
        }
    }
}
