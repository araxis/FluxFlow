using System.Collections.Immutable;

namespace FluxFlow.Data;

public interface IFlowContentCodec
{
    FlowValue Decode(ImmutableArray<byte> content, string? encoding);
}
