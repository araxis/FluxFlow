using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Storage.Nodes;

internal static class StorageStoreNodeFactory
{
    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        IStorageStoreFactory storeFactory,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(storeFactory);
        ArgumentNullException.ThrowIfNull(clock);

        var options = StorageOptionsReader.ReadStoreOptions(context.Definition);
        var node = new StorageStoreNode(
            context.Address,
            string.IsNullOrWhiteSpace(options.StoreName)
                ? context.Address.Node.Value
                : options.StoreName,
            storeFactory,
            clock);

        return context.CreateNode(node).Build();
    }
}
