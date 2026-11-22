using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Http;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Serialization,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HttpSendRequest))]
[JsonSerializable(typeof(HttpResponse))]
internal sealed partial class HttpJsonContext : JsonSerializerContext
{
    // Generated deserialization on the supported targets overwrites omitted init-only
    // defaults. Keep the native reflection reader to preserve Method and Headers.
    internal static readonly JsonSerializerOptions RequestReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
