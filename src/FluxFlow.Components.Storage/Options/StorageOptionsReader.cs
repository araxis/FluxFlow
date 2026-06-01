using FluxFlow.Engine.Definitions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Storage.Options;

internal static class StorageOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static StoragePutOptions ReadPutOptions(NodeDefinition definition)
    {
        var options = Read<StoragePutOptions>(definition);
        ValidateBoundedCapacity("storage.put", options.BoundedCapacity);
        ValidateOptionalText("storage.put", "store", options.Store);
        ValidateOptionalText("storage.put", "collection", options.Collection);
        return options;
    }

    public static StorageGetOptions ReadGetOptions(NodeDefinition definition)
    {
        var options = Read<StorageGetOptions>(definition);
        ValidateBoundedCapacity("storage.get", options.BoundedCapacity);
        ValidateOptionalText("storage.get", "store", options.Store);
        ValidateOptionalText("storage.get", "collection", options.Collection);
        return options;
    }

    public static StorageDeleteOptions ReadDeleteOptions(NodeDefinition definition)
    {
        var options = Read<StorageDeleteOptions>(definition);
        ValidateBoundedCapacity("storage.delete", options.BoundedCapacity);
        ValidateOptionalText("storage.delete", "store", options.Store);
        ValidateOptionalText("storage.delete", "collection", options.Collection);
        return options;
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }

    private static void ValidateBoundedCapacity(string nodeType, int boundedCapacity)
    {
        if (boundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'boundedCapacity' must be greater than zero.");
        }
    }

    private static void ValidateOptionalText(
        string nodeType,
        string optionName,
        string? value)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{nodeType} option '{optionName}' cannot be empty when set.");
        }
    }
}
