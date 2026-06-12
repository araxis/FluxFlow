using FluxFlow.Components.Timers.Timing;
using System.Text.Json;

namespace FluxFlow.Components.Timers.Options;

public sealed class TimerComponentOptions
{
    private ITimerClock _clock = SystemTimerClock.Instance;
    private readonly object _typesLock = new();
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [TimerDelayOptions.ObjectTypeName] = typeof(object),
        [typeof(object).FullName!] = typeof(object),
        ["string"] = typeof(string),
        [typeof(string).FullName!] = typeof(string),
        ["bool"] = typeof(bool),
        [typeof(bool).FullName!] = typeof(bool),
        ["int"] = typeof(int),
        [typeof(int).FullName!] = typeof(int),
        ["long"] = typeof(long),
        [typeof(long).FullName!] = typeof(long),
        ["double"] = typeof(double),
        [typeof(double).FullName!] = typeof(double),
        ["decimal"] = typeof(decimal),
        [typeof(decimal).FullName!] = typeof(decimal),
        ["bytes"] = typeof(byte[]),
        [typeof(byte[]).FullName!] = typeof(byte[]),
        ["json"] = typeof(JsonElement),
        [nameof(JsonElement)] = typeof(JsonElement),
        [typeof(JsonElement).FullName!] = typeof(JsonElement)
    };

    public ITimerClock Clock => _clock;

    public TimerComponentOptions UseClock(ITimerClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    public TimerComponentOptions RegisterType<T>(string name)
        => RegisterType(name, typeof(T));

    public TimerComponentOptions RegisterType(string name, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        _types[name.Trim()] = type;
        _types[type.FullName ?? type.Name] = type;
        return this;
    }

    internal Type ResolveType(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = name.Trim();

        lock (_typesLock)
        {
            if (_types.TryGetValue(key, out var type))
            {
                return type;
            }

            var resolved = Type.GetType(key, throwOnError: false, ignoreCase: false);
            if (resolved is not null)
            {
                _types[key] = resolved;
                return resolved;
            }
        }

        throw new InvalidOperationException(
            $"Timer type '{name}' is not registered.");
    }
}
