using System.Text.Json;

namespace FluxFlow.Components.Sources.Options;

public sealed class SourcesComponentOptions
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [GeneratedSourceOptions.ObjectTypeName] = typeof(object),
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

    private JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public SourcesComponentOptions RegisterType<T>(string name)
        => RegisterType(name, typeof(T));

    public SourcesComponentOptions RegisterType(string name, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);

        _types[name.Trim()] = type;
        _types[type.FullName ?? type.Name] = type;
        return this;
    }

    public SourcesComponentOptions UseJsonSerializerOptions(JsonSerializerOptions serializerOptions)
    {
        _serializerOptions = serializerOptions ?? throw new ArgumentNullException(nameof(serializerOptions));
        return this;
    }

    internal Type ResolveType(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = name.Trim();
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

        throw new InvalidOperationException(
            $"source.generated output type '{name}' is not registered.");
    }

    internal IReadOnlyList<TOutput> DeserializeItems<TOutput>(GeneratedSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var items = new List<TOutput>(options.Items.Length);
        for (var index = 0; index < options.Items.Length; index++)
        {
            try
            {
                items.Add(DeserializeItem<TOutput>(options.Items[index]));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"source.generated item at index {index} could not be converted to '{typeof(TOutput).Name}'.",
                    exception);
            }
        }

        return items;
    }

    private TOutput DeserializeItem<TOutput>(JsonElement item)
    {
        if (typeof(TOutput) == typeof(object) || typeof(TOutput) == typeof(JsonElement))
        {
            return (TOutput)(object)item.Clone();
        }

        return item.Deserialize<TOutput>(_serializerOptions)
            ?? throw new InvalidOperationException("Deserialized source item was null.");
    }
}
