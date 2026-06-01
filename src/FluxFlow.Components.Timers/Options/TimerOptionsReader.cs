using FluxFlow.Engine.Definitions;
using System.Text.Json;

namespace FluxFlow.Components.Timers.Options;

internal static class TimerOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static TimerIntervalSettings ReadIntervalSettings(NodeDefinition definition)
    {
        var options = Read<TimerIntervalOptions>(definition);

        ValidateBoundedCapacity("timer.interval", options.BoundedCapacity);
        var interval = ResolveDuration(
            "timer.interval",
            "interval",
            options.Interval,
            "intervalMilliseconds",
            options.IntervalMilliseconds,
            required: true);
        if (interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "timer.interval option 'interval' must be greater than zero.");
        }

        var initialDelay = ResolveDuration(
            "timer.interval",
            "initialDelay",
            options.InitialDelay,
            "initialDelayMilliseconds",
            options.InitialDelayMilliseconds,
            required: false);
        if (initialDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "timer.interval option 'initialDelay' cannot be negative.");
        }

        if (options.MaxTicks is <= 0)
        {
            throw new InvalidOperationException(
                "timer.interval option 'maxTicks' must be greater than zero when set.");
        }

        return new TimerIntervalSettings
        {
            Name = string.IsNullOrWhiteSpace(options.Name) ? "interval" : options.Name.Trim(),
            Interval = interval,
            InitialDelay = initialDelay,
            EmitImmediately = options.EmitImmediately,
            MaxTicks = options.MaxTicks,
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

    private static TimeSpan ResolveDuration(
        string nodeType,
        string durationName,
        TimeSpan? duration,
        string millisecondsName,
        double? milliseconds,
        bool required)
    {
        if (duration.HasValue && milliseconds.HasValue)
        {
            throw new InvalidOperationException(
                $"{nodeType} cannot set both '{durationName}' and '{millisecondsName}'.");
        }

        if (duration.HasValue)
        {
            return duration.Value;
        }

        if (milliseconds.HasValue)
        {
            if (double.IsNaN(milliseconds.Value) || double.IsInfinity(milliseconds.Value))
            {
                throw new InvalidOperationException(
                    $"{nodeType} option '{millisecondsName}' must be a finite number.");
            }

            return TimeSpan.FromMilliseconds(milliseconds.Value);
        }

        if (required)
        {
            throw new InvalidOperationException(
                $"{nodeType} requires '{durationName}' or '{millisecondsName}'.");
        }

        return TimeSpan.Zero;
    }

    private static void ValidateBoundedCapacity(string nodeType, int boundedCapacity)
    {
        if (boundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'boundedCapacity' must be greater than zero.");
        }
    }
}
