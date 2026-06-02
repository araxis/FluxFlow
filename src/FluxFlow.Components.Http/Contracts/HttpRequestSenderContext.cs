using FluxFlow.Components.Http.Options;
using FluxFlow.Components.Http.Timing;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Http.Contracts;

public sealed record HttpRequestSenderContext
{
    public required NodeAddress Address { get; init; }
    public required HttpRequestNodeOptions Options { get; init; }
    public IHttpClock Clock { get; init; } = SystemHttpClock.Instance;
}
