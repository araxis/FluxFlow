using FluxFlow.Components.Storage.Timing;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Storage.Contracts;

public sealed record StorageStoreContext
{
    public required NodeAddress Address { get; init; }
    public required NodeType NodeType { get; init; }
    public string? StoreName { get; init; }
    public string? Collection { get; init; }
    public IStorageClock Clock { get; init; } = SystemStorageClock.Instance;
}
