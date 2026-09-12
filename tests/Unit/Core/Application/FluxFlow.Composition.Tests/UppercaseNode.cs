using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Composition.Tests;

internal sealed class UppercaseNode : FlowNode
{
    protected override async Task ProcessAsync(FlowMessage message)
        => await EmitAsync(
                message.With(FlowValue.From(message.Value!.Deserialize<string>()!.ToUpperInvariant())),
                Stopping)
            .ConfigureAwait(false);
}
