using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Storage;

public static class StorageComponentTypes
{
    public static readonly NodeType Store = new("storage.store");
    public static readonly NodeType Put = new("storage.put");
    public static readonly NodeType Get = new("storage.get");
    public static readonly NodeType Query = new("storage.query");
    public static readonly NodeType Delete = new("storage.delete");
}
