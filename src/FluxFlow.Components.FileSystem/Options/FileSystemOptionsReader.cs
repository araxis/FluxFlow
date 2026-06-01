using FluxFlow.Engine.Definitions;
using System.Text;
using System.Text.Json;

namespace FluxFlow.Components.FileSystem.Options;

internal static class FileSystemOptionsReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static FileWriteOptions ReadFileWriteOptions(NodeDefinition definition)
    {
        var options = Read<FileWriteOptions>(definition);

        if (options.BoundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                "file.write option 'boundedCapacity' must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultEncoding))
        {
            throw new InvalidOperationException(
                "file.write option 'defaultEncoding' cannot be empty.");
        }

        try
        {
            Encoding.GetEncoding(options.DefaultEncoding);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "file.write option 'defaultEncoding' is not supported.",
                exception);
        }

        return options;
    }

    private static T Read<T>(NodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition.Configuration, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not read {typeof(T).Name}.");
    }
}
