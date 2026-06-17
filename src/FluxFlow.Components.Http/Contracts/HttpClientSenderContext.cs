using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Http.Contracts;

/// <summary>
/// Client-scoped build context for a shared request sender. Unlike the
/// request-scoped <see cref="HttpRequestSenderContext"/>, this carries no
/// per-request node options and no per-request address: the sender is built
/// once at connect-time from the owning http.client handle's configuration and
/// then borrowed by request nodes at call-time.
/// </summary>
public sealed record HttpClientSenderContext
{
    public required NodeAddress Address { get; init; }
    public required IHttpClientHandle Client { get; init; }
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}
