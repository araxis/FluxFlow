using FluxFlow.Components.Storage.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Storage.Nodes;

internal static class StorageStoreNodeFactory
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = StorageOptionsReader.ReadStoreOptions(context.Definition);
        var node = new StorageStoreNode(
            string.IsNullOrWhiteSpace(options.StoreName)
                ? context.Address.Node.Value
                : options.StoreName);

        return context.CreateNode(node).Build();
    }
}
