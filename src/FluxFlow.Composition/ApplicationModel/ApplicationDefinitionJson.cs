using System.Text.Json;

namespace FluxFlow.Composition.Model;

public static class ApplicationDefinitionJson
{
    private static readonly JsonSerializerOptions CompactSerializerOptions = CreateSerializerOptions();

    private static readonly JsonSerializerOptions IndentedSerializerOptions =
        CreateSerializerOptions(writeIndented: true);

    public static JsonSerializerOptions CreateSerializerOptions(bool writeIndented = false)
        => new(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = false,
            WriteIndented = writeIndented
        };

    public static ApplicationDefinition Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ApplicationDefinition>(json, CompactSerializerOptions)
            ?? throw new JsonException("Application definition cannot be null.");
    }

    public static ApplicationDefinition Deserialize(ReadOnlySpan<byte> utf8Json)
        => JsonSerializer.Deserialize<ApplicationDefinition>(utf8Json, CompactSerializerOptions)
            ?? throw new JsonException("Application definition cannot be null.");

    public static string Serialize(ApplicationDefinition definition, bool writeIndented = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, ResolveSerializerOptions(writeIndented));
    }

    public static byte[] SerializeToUtf8Bytes(
        ApplicationDefinition definition,
        bool writeIndented = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.SerializeToUtf8Bytes(
            definition,
            ResolveSerializerOptions(writeIndented));
    }

    private static JsonSerializerOptions ResolveSerializerOptions(bool writeIndented)
        => writeIndented ? IndentedSerializerOptions : CompactSerializerOptions;
}
