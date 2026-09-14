using System.Text.Json;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Composition.Tests;

internal sealed class UppercaseNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(
                message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value!.Json.Deserialize<string>()!.ToUpperInvariant(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
                Stopping)
            .ConfigureAwait(false);
}
