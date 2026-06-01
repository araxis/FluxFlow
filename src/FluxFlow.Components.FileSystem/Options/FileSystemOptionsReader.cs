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

        ValidateBoundedCapacity("file.write", options.BoundedCapacity);
        ValidateDefaultEncoding("file.write", options.DefaultEncoding);

        return options;
    }

    public static FileReadOptions ReadFileReadOptions(NodeDefinition definition)
    {
        var options = Read<FileReadOptions>(definition);

        ValidateBoundedCapacity("file.read", options.BoundedCapacity);
        ValidateDefaultEncoding("file.read", options.DefaultEncoding);
        if (options.MaxBytes is <= 0)
        {
            throw new InvalidOperationException(
                "file.read option 'maxBytes' must be greater than zero when set.");
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

    private static void ValidateBoundedCapacity(string nodeType, int boundedCapacity)
    {
        if (boundedCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'boundedCapacity' must be greater than zero.");
        }
    }

    private static void ValidateDefaultEncoding(string nodeType, string defaultEncoding)
    {
        if (string.IsNullOrWhiteSpace(defaultEncoding))
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'defaultEncoding' cannot be empty.");
        }

        try
        {
            Encoding.GetEncoding(defaultEncoding);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{nodeType} option 'defaultEncoding' is not supported.",
                exception);
        }
    }
}
