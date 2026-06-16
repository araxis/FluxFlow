using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Engine.Components;

namespace FluxFlow.Components.Storage.Nodes;

public sealed class StorageStoreNode : FlowNodeBase, IStorageStoreHandle
{
    public StorageStoreNode(string storeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);

        StoreName = storeName;
    }

    public string StoreName { get; }
}
