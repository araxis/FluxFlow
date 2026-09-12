using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Storage.Designer;

public static class StorageDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddStorageDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(StorageComponentDefinition.Types.Put, DescribePut)
            .AddDesignMetadata(StorageComponentDefinition.Types.Get, DescribeGet)
            .AddDesignMetadata(StorageComponentDefinition.Types.Query, DescribeQuery)
            .AddDesignMetadata(StorageComponentDefinition.Types.Delete, DescribeDelete);
    }

    private static ComponentDesignMetadata DescribePut(ComponentDesignMetadata metadata)
    {
        var defaults = new StoragePutOptions();
        metadata = metadata.WithDisplay("Storage Put", "Storage", "Stores exact content through a host-owned storage store.", "database-plus", "putRecord", 460);
        metadata = metadata.WithResource(StorageComponentDefinition.Resources.Store, "Store", 0, "Required keyed storage store or store factory used for put, get, query, and delete operations.", designValueType: "IStorageStore or IStorageStoreFactory", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Store, keyPattern: "storage-store:{name}");
        metadata = metadata.WithResource(StorageComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic storage diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        metadata = metadata.WithOption(StorageComponentDefinition.Options.Collection, OptionValueKind.Text, "Collection", "Default collection used when the input request does not specify one.", section: "Collection", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.Mode, OptionValueKind.Enum, "Mode", "Write behavior when a record already exists or is missing.", defaultValue: defaults.Mode.ToString(), section: "Write", importance: OptionDesignMetadataAttributeValues.Primary);
        metadata = metadata.WithOptionChoice(StorageComponentDefinition.Options.Mode, StorageWriteMode.Upsert.ToString(), "Upsert", "Create or replace the record.");
        metadata = metadata.WithOptionChoice(StorageComponentDefinition.Options.Mode, StorageWriteMode.Create.ToString(), "Create", "Fail when the record already exists.");
        metadata = metadata.WithOptionChoice(StorageComponentDefinition.Options.Mode, StorageWriteMode.Replace.ToString(), "Replace", "Fail when the record does not exist.");
        metadata = metadata.WithOption(StorageComponentDefinition.Options.EmitStoredRecord, OptionValueKind.Boolean, "Emit Stored Record", "Include the stored record in the output result.", defaultValue: defaults.EmitStoredRecord, section: "Results", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(StorageComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Exact-content storage put request.", true)
            .WithPort(StorageComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Stored or failed operation result.", true)
            .WithPort(StorageComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort storage-put diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeGet(ComponentDesignMetadata metadata)
    {
        var defaults = new StorageGetOptions();
        metadata = metadata.WithDisplay("Storage Get", "Storage", "Reads exact content and returns found, missing, or failed results.", "database-search", "getRecord", 460);
        metadata = metadata.WithResource(StorageComponentDefinition.Resources.Store, "Store", 0, "Required keyed storage store or store factory used for put, get, query, and delete operations.", designValueType: "IStorageStore or IStorageStoreFactory", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Store, keyPattern: "storage-store:{name}");
        metadata = metadata.WithResource(StorageComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic storage diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        metadata = metadata.WithOption(StorageComponentDefinition.Options.Collection, OptionValueKind.Text, "Collection", "Default collection used when the input request does not specify one.", section: "Collection", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.IncludeExpired, OptionValueKind.Boolean, "Include Expired", "Include records that the store considers expired.", defaultValue: defaults.IncludeExpired, section: "Expiration", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(StorageComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Storage get request.", true)
            .WithPort(StorageComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Found, missing, or failed operation result.", true)
            .WithPort(StorageComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort storage-get diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeQuery(ComponentDesignMetadata metadata)
    {
        var defaults = new StorageQueryOptions();
        metadata = metadata.WithDisplay("Storage Query", "Storage", "Queries exact-content records and returns one result.", "database", "queryRecords", 460);
        metadata = metadata.WithResource(StorageComponentDefinition.Resources.Store, "Store", 0, "Required keyed storage store or store factory used for put, get, query, and delete operations.", designValueType: "IStorageStore or IStorageStoreFactory", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Store, keyPattern: "storage-store:{name}");
        metadata = metadata.WithResource(StorageComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic storage diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        metadata = metadata.WithOption(StorageComponentDefinition.Options.Collection, OptionValueKind.Text, "Collection", "Default collection used when the input request does not specify one.", section: "Collection", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.IncludeExpired, OptionValueKind.Boolean, "Include Expired", "Include records that the store considers expired.", defaultValue: defaults.IncludeExpired, section: "Expiration", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.Offset, OptionValueKind.Number, "Offset", "Number of matched records to skip.", defaultValue: defaults.Offset, min: 0, section: "Query", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.Limit, OptionValueKind.Number, "Limit", "Maximum number of records to return.", defaultValue: defaults.Limit, min: 1, section: "Query", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.EmitRecordsInResult, OptionValueKind.Boolean, "Emit Records In Result", "Include matched records in the query result payload.", defaultValue: defaults.EmitRecordsInResult, section: "Results", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(StorageComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Storage query request.", true)
            .WithPort(StorageComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Completed or failed storage query result.", true)
            .WithPort(StorageComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort storage-query diagnostics.");
        return metadata;
    }

    private static ComponentDesignMetadata DescribeDelete(ComponentDesignMetadata metadata)
    {
        var defaults = new StorageDeleteOptions();
        metadata = metadata.WithDisplay("Storage Delete", "Storage", "Deletes a record through a host-owned storage store.", "database-x", "deleteRecord", 460);
        metadata = metadata.WithResource(StorageComponentDefinition.Resources.Store, "Store", 0, "Required keyed storage store or store factory used for put, get, query, and delete operations.", designValueType: "IStorageStore or IStorageStoreFactory", ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Store, keyPattern: "storage-store:{name}");
        metadata = metadata.WithResource(StorageComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic storage diagnostics and timestamps.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "clock:{name}");
        metadata = metadata.WithOption(StorageComponentDefinition.Options.Collection, OptionValueKind.Text, "Collection", "Default collection used when the input request does not specify one.", section: "Collection", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(StorageComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata
            .WithPort(StorageComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Storage delete request.", true)
            .WithPort(StorageComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Deleted, missing, or failed operation result.", true)
            .WithPort(StorageComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort storage-delete diagnostics.");
        return metadata;
    }
}
