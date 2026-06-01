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

    public static TimerDelaySettings ReadDelaySettings(NodeDefinition definition)
    {
        var options = Read<TimerDelayOptions>(definition);

        ValidateBoundedCapacity("timer.delay", options.BoundedCapacity);
        if (string.IsNullOrWhiteSpace(options.InputType))
        {
            throw new InvalidOperationException(
                "timer.delay option 'inputType' cannot be empty.");
        }

        var delay = ResolveDuration(
            "timer.delay",
            "delay",
            options.Delay,
            "delayMilliseconds",
            options.DelayMilliseconds,
            required: true);
        if (delay < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "timer.delay option 'delay' cannot be negative.");
        }

        return new TimerDelaySettings
        {
            Name = string.IsNullOrWhiteSpace(options.Name) ? "delay" : options.Name.Trim(),
            InputType = options.InputType.Trim(),
            Delay = delay,
            BoundedCapacity = options.BoundedCapacity
        };
    }

    public static TimerScheduleSettings ReadScheduleSettings(NodeDefinition definition)
    {
        var options = Read<TimerScheduleOptions>(definition);

        ValidateBoundedCapacity("timer.schedule", options.BoundedCapacity);
        var cron = ResolveScheduleExpression(options);
        var timeZone = ResolveTimeZone(options.TimeZoneId);
        if (options.MaxTicks is <= 0)
        {
            throw new InvalidOperationException(
                "timer.schedule option 'maxTicks' must be greater than zero when set.");
        }

        return new TimerScheduleSettings
        {
            Name = string.IsNullOrWhiteSpace(options.Name) ? "schedule" : options.Name.Trim(),
            Cron = cron,
            Schedule = CronSchedule.Parse(cron),
            TimeZone = timeZone,
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

            try
            {
                return TimeSpan.FromMilliseconds(milliseconds.Value);
            }
            catch (OverflowException exception)
            {
                throw new InvalidOperationException(
                    $"{nodeType} option '{millisecondsName}' is outside the supported range.",
                    exception);
            }
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

    private static string ResolveScheduleExpression(TimerScheduleOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Cron) &&
            !string.IsNullOrWhiteSpace(options.Expression))
        {
            throw new InvalidOperationException(
                "timer.schedule cannot set both 'cron' and 'expression'.");
        }

        var cron = string.IsNullOrWhiteSpace(options.Cron)
            ? options.Expression
            : options.Cron;
        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new InvalidOperationException(
                "timer.schedule requires 'cron' or 'expression'.");
        }

        return cron.Trim();
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) ||
            timeZoneId.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"timer.schedule option 'timeZoneId' was not found: '{timeZoneId}'.",
                exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new InvalidOperationException(
                $"timer.schedule option 'timeZoneId' is invalid: '{timeZoneId}'.",
                exception);
        }
    }
}
