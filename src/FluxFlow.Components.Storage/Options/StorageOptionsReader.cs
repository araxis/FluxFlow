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

    public static StorageStoreOptions ReadStoreOptions(NodeDefinition definition)
    {
        var options = Read<StorageStoreOptions>(definition);
        ValidateOptionalText("storage.store", "storeName", options.StoreName);
        return options;
    }

    public static StoragePutOptions ReadPutOptions(NodeDefinition definition)
    {
        var options = Read<StoragePutOptions>(definition);
        ValidateBoundedCapacity("storage.put", options.BoundedCapacity);
        ValidateRequiredStore("storage.put", options.Store);
        ValidateOptionalText("storage.put", "collection", options.Collection);
        return options;
    }

    public static StorageGetOptions ReadGetOptions(NodeDefinition definition)
    {
        var options = Read<StorageGetOptions>(definition);
        ValidateBoundedCapacity("storage.get", options.BoundedCapacity);
        ValidateRequiredStore("storage.get", options.Store);
        ValidateOptionalText("storage.get", "collection", options.Collection);
        return options;
    }

    public static StorageDeleteOptions ReadDeleteOptions(NodeDefinition definition)
    {
        var options = Read<StorageDeleteOptions>(definition);
        ValidateBoundedCapacity("storage.delete", options.BoundedCapacity);
        ValidateRequiredStore("storage.delete", options.Store);
        ValidateOptionalText("storage.delete", "collection", options.Collection);
        return options;
    }

    public static StorageQueryOptions ReadQueryOptions(NodeDefinition definition)
    {
        var options = Read<StorageQueryOptions>(definition);
        ValidateBoundedCapacity("storage.query", options.BoundedCapacity);
        ValidateRequiredStore("storage.query", options.Store);
        ValidateOptionalText("storage.query", "collection", options.Collection);
        ValidateOffset("storage.query", options.Offset);
        ValidateLimit("storage.query", options.Limit);
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

    private static void ValidateRequiredStore(string nodeType, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'store' is required and must name a storage.store resource.");
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

    private static void ValidateLimit(string nodeType, int limit)
    {
        if (limit <= 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'limit' must be greater than zero.");
        }
    }

    private static void ValidateOffset(string nodeType, int offset)
    {
        if (offset < 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'offset' cannot be negative.");
        }
    }
}
