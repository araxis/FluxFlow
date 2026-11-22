using System.Text.Json;

namespace FluxFlow.Authoring.Definitions;

internal static class RabbitDeclarationDefaults
{
    internal static JsonElement Empty { get; } = Create();

    private static JsonElement Create()
    {
        using var json = JsonDocument.Parse("""{"Exchanges":[],"Queues":[],"Bindings":[]}""");
        return json.RootElement.Clone();
    }
}
