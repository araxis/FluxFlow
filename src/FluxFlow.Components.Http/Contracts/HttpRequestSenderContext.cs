using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Http.Contracts;

public sealed record HttpRequestSenderContext
{
    public required NodeAddress Address { get; init; }
    public required HttpRequestNodeOptions Options { get; init; }
    public required IHttpClientHandle Client { get; init; }
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}
