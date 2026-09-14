using System.Text.Json;
using FluxFlow.Core;

namespace FluxFlow.Json;

internal sealed record CallFunctionConfiguration(string RequestTo, string ReplyFrom, TimeSpan Timeout)
{
    internal CallFunction Create() => new(RequestTo, ReplyFrom, Timeout);

    internal static CallFunction Read(IReadOnlyDictionary<string, JsonElement> properties)
        => JsonSerializer.SerializeToElement(properties).Deserialize<CallFunctionConfiguration>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Create();
}
