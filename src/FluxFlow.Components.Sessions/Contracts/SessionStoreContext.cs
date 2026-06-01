using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Sessions.Contracts;

public sealed record SessionStoreContext
{
    public required NodeAddress Address { get; init; }
    public required NodeType NodeType { get; init; }
    public string? StoreName { get; init; }
    public string? SessionId { get; init; }
}
