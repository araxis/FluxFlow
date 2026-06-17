using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Storage;

public sealed class StorageComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = StorageComponentTypes.Store,
            DisplayName = "Storage Store",
            Category = "Storage",
            Summary = "Owns a shared storage store referenced by storage operation nodes. The host opens and closes the store via the explicit ConnectAsync/DisconnectAsync API; there is no auto-open.",
            IconKey = "storage",
            PreferredNodeName = "storageStore",
            SuggestedEditorWidth = 460,
            Options =
            [
                TextHelper("storeName", "Store name", "Logical store name; defaults to the resource node name when omitted.")
            ],
            Ports =
            [
                Port(StorageComponentPorts.Errors, PortDirection.Output, "FlowError", false)
            ]
        },
        Metadata(StorageComponentTypes.Put, "Storage Put", "storagePut", "Writes records into a host-provided storage store.",
            "StoragePutRequest", StorageComponentPorts.Result, "StoragePutResult"),
        Metadata(StorageComponentTypes.Get, "Storage Get", "storageGet", "Reads a record from a host-provided storage store.",
            "StorageGetRequest", StorageComponentPorts.Found, "StorageRecord"),
        Metadata(StorageComponentTypes.Query, "Storage Query", "storageQuery", "Queries records from a host-provided storage store.",
            "StorageQueryRequest", StorageComponentPorts.Records, "StorageRecord"),
        Metadata(StorageComponentTypes.Delete, "Storage Delete", "storageDelete", "Deletes records from a host-provided storage store.",
            "StorageDeleteRequest", StorageComponentPorts.Result, "StorageDeleteResult")
    ];

    private static ComponentDesignMetadata Metadata(
        NodeType type,
        string displayName,
        string preferredName,
        string summary,
        string inputType,
        string outputPort,
        string outputType) => new()
        {
            Type = type,
            DisplayName = displayName,
            Category = "Storage",
            Summary = summary,
            IconKey = "storage",
            PreferredNodeName = preferredName,
            SuggestedEditorWidth = 480,
            Options =
            [
                TextRequired("store", "Store name", "Name of the storage.store resource to use."),
                Text("collection", "Collection", "default"),
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(StorageComponentPorts.Input, PortDirection.Input, inputType, true),
                Port(outputPort, PortDirection.Output, outputType, true, 1),
                Port(StorageComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        };

    private static OptionDesignMetadata Text(string name, string displayName, object defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata TextHelper(string name, string displayName, string helperText) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        HelperText = helperText
    };

    private static OptionDesignMetadata TextRequired(string name, string displayName, string helperText) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        HelperText = helperText,
        IsRequired = true
    };

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static PortDesignMetadata Port(string name, PortDirection direction, string valueType, bool primary, int order = 0) => new()
    {
        Name = new PortName(name),
        Direction = direction,
        ValueType = valueType,
        IsPrimary = primary,
        Order = order
    };
}
