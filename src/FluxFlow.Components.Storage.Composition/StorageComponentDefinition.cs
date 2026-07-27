using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Options;

namespace FluxFlow.Components.Storage.Composition;

public static partial class StorageComponentDefinition
{
    private static readonly StoragePutOptions PutDefaults = new();
    private static readonly StorageGetOptions GetDefaults = new();
    private static readonly StorageQueryOptions QueryDefaults = new();
    private static readonly StorageDeleteOptions DeleteDefaults = new();

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        =>
        [
            CreatePutMetadata(),
            CreateGetMetadata(),
            CreateQueryMetadata(),
            CreateDeleteMetadata()
        ];

    private static ComponentDesignMetadata CreatePutMetadata()
    {
        var builder = CreateStorageMetadataBuilder(
            StorageComponentDefinition.Types.Put,
            "Storage Put",
            "Stores exact content through a host-owned storage store.",
            "database-plus",
            "putRecord");

        builder
            .AddOption(CollectionOption())
            .AddOption(
                Options.Mode,
                OptionValueKind.Enum,
                displayName: "Mode",
                helperText: "Write behavior when a record already exists or is missing.",
                defaultValue: PutDefaults.Mode.ToString(),
                choices: WriteModeChoices(),
                attributes: OptionAttributes(
                    "Write",
                    OptionDesignMetadataAttributeValues.Primary))
            .AddOption(
                Options.EmitStoredRecord,
                OptionValueKind.Boolean,
                displayName: "Emit Stored Record",
                helperText: "Include the stored record in the output result.",
                defaultValue: PutDefaults.EmitStoredRecord,
                attributes: OptionAttributes(
                    "Results",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(BoundedCapacityOption(PutDefaults.BoundedCapacity));

        AddTransformPorts(
            builder,
            nameof(StorageContentPutRequest),
            "Exact-content storage put request.",
            nameof(StoragePutOutcome),
            "Stored or failed operation result.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateGetMetadata()
    {
        var builder = CreateStorageMetadataBuilder(
            StorageComponentDefinition.Types.Get,
            "Storage Get",
            "Reads exact content and returns found, missing, or failed results.",
            "database-search",
            "getRecord");

        builder
            .AddOption(CollectionOption())
            .AddOption(IncludeExpiredOption(GetDefaults.IncludeExpired))
            .AddOption(BoundedCapacityOption(GetDefaults.BoundedCapacity));

        AddTransformPorts(
            builder,
            nameof(StorageGetRequest),
            "Storage get request.",
            nameof(StorageGetOutcome),
            "Found, missing, or failed operation result.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateQueryMetadata()
    {
        var builder = CreateStorageMetadataBuilder(
            StorageComponentDefinition.Types.Query,
            "Storage Query",
            "Queries exact-content records and returns one result.",
            "database",
            "queryRecords");

        builder
            .AddOption(CollectionOption())
            .AddOption(IncludeExpiredOption(QueryDefaults.IncludeExpired))
            .AddOption(
                Options.Offset,
                OptionValueKind.Number,
                displayName: "Offset",
                helperText: "Number of matched records to skip.",
                defaultValue: QueryDefaults.Offset,
                min: 0,
                attributes: OptionAttributes(
                    "Query",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.Limit,
                OptionValueKind.Number,
                displayName: "Limit",
                helperText: "Maximum number of records to return.",
                defaultValue: QueryDefaults.Limit,
                min: 1,
                attributes: OptionAttributes(
                    "Query",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                Options.EmitRecordsInResult,
                OptionValueKind.Boolean,
                displayName: "Emit Records In Result",
                helperText: "Include matched records in the query result payload.",
                defaultValue: QueryDefaults.EmitRecordsInResult,
                attributes: OptionAttributes(
                    "Results",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(BoundedCapacityOption(QueryDefaults.BoundedCapacity));

        builder
            .AddInputPort(
                StorageComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "Storage query request.",
                valueType: nameof(StorageQueryRequest),
                isPrimary: true)
            .AddOutputPort(
                StorageComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "Completed or failed storage query result.",
                valueType: nameof(StorageQueryOutcome),
                isPrimary: true);

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateDeleteMetadata()
    {
        var builder = CreateStorageMetadataBuilder(
            StorageComponentDefinition.Types.Delete,
            "Storage Delete",
            "Deletes a record through a host-owned storage store.",
            "database-x",
            "deleteRecord");

        builder
            .AddOption(CollectionOption())
            .AddOption(BoundedCapacityOption(DeleteDefaults.BoundedCapacity));

        AddTransformPorts(
            builder,
            nameof(StorageDeleteRequest),
            "Storage delete request.",
            nameof(StorageDeleteOutcome),
            "Deleted, missing, or failed operation result.");

        return builder.Build();
    }

    private static ComponentDesignMetadataBuilder CreateStorageMetadataBuilder(
        string type,
        string displayName,
        string summary,
        string iconKey,
        string preferredNodeName)
        => new ComponentDesignMetadataBuilder(type)
            .WithDisplay(
                displayName: displayName,
                category: "Storage",
                summary: summary,
                iconKey: iconKey,
                preferredNodeName: preferredNodeName,
                suggestedEditorWidth: 460)
            .AddResource(ResourceDesignMetadataFactory.HostOwned(
                StorageComponentDefinition.Resources.Store,
                ResourceDesignMetadataAttributeValues.Store,
                "Store",
                0,
                "Required keyed storage store or store factory used for put, get, query, and delete operations.",
                $"{nameof(IStorageStore)} or {nameof(IStorageStoreFactory)}",
                isRequired: true,
                keyPattern: "storage-store:{name}"))
            .AddResource(ResourceDesignMetadataFactory.Clock(
                StorageComponentDefinition.Resources.Clock,
                1,
                "Optional keyed clock for deterministic storage diagnostics and timestamps."));

    private static OptionDesignMetadata CollectionOption() => new()
    {
        Name = new ComponentOptionName(Options.Collection),
        Kind = OptionValueKind.Text,
        DisplayName = new ComponentMetadataText("Collection"),
        HelperText = new ComponentMetadataText("Default collection used when the input request does not specify one."),
        Attributes = OptionAttributeMap(
            "Collection",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Text)
    };

    private static OptionDesignMetadata IncludeExpiredOption(bool defaultValue) => new()
    {
        Name = new ComponentOptionName(Options.IncludeExpired),
        Kind = OptionValueKind.Boolean,
        DisplayName = new ComponentMetadataText("Include Expired"),
        DefaultValue = defaultValue,
        HelperText = new ComponentMetadataText("Include records that the store considers expired."),
        Attributes = OptionAttributeMap(
            "Expiration",
            OptionDesignMetadataAttributeValues.Advanced)
    };

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue)
        => OptionDesignMetadataFactory.BoundedCapacity(defaultValue);

    private static IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> OptionAttributeMap(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.CreateMap(
            section: section,
            importance: importance,
            editor: editor);

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static IReadOnlyList<OptionChoiceMetadata> WriteModeChoices()
        =>
        [
            WriteModeChoice(StorageWriteMode.Upsert, "Upsert", "Create or replace the record."),
            WriteModeChoice(StorageWriteMode.Create, "Create", "Fail when the record already exists."),
            WriteModeChoice(StorageWriteMode.Replace, "Replace", "Fail when the record does not exist.")
        ];

    private static OptionChoiceMetadata WriteModeChoice(
        StorageWriteMode mode,
        string displayName,
        string helperText) => new()
        {
            Value = new ComponentOptionChoiceValue(mode.ToString()),
            DisplayName = new ComponentMetadataText(displayName),
            HelperText = new ComponentMetadataText(helperText)
        };

    private static void AddTransformPorts(
        ComponentDesignMetadataBuilder builder,
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        => builder
            .AddInputPort(
                StorageComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                StorageComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: outputSummary,
                valueType: outputType,
                isPrimary: true);


    public static class Options
    {
        public const string Collection = "collection";
        public const string Mode = "mode";
        public const string EmitStoredRecord = "emitStoredRecord";
        public const string BoundedCapacity = "boundedCapacity";
        public const string IncludeExpired = "includeExpired";
        public const string Offset = "offset";
        public const string Limit = "limit";
        public const string EmitRecordsInResult = "emitRecordsInResult";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Put =>
            [
                ComponentOptions.Metadata<string>(Options.Collection),
                ComponentOptions.Metadata<StorageWriteMode>(Options.Mode),
                ComponentOptions.Metadata<bool>(Options.EmitStoredRecord),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Get =>
            [
                ComponentOptions.Metadata<string>(Options.Collection),
                ComponentOptions.Metadata<bool>(Options.IncludeExpired),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Query =>
            [
                ComponentOptions.Metadata<string>(Options.Collection),
                ComponentOptions.Metadata<bool>(Options.IncludeExpired),
                ComponentOptions.Metadata<int>(Options.Offset),
                ComponentOptions.Metadata<int>(Options.Limit),
                ComponentOptions.Metadata<bool>(Options.EmitRecordsInResult),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            Types.Delete =>
            [
                ComponentOptions.Metadata<string>(Options.Collection),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Put =>
            [
                ComponentResources.Metadata<IStorageStore>(Resources.Store, isRequired: true, valueTypeHint: "IStorageStore or IStorageStoreFactory"),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Get =>
            [
                ComponentResources.Metadata<IStorageStore>(Resources.Store, isRequired: true, valueTypeHint: "IStorageStore or IStorageStoreFactory"),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Query =>
            [
                ComponentResources.Metadata<IStorageStore>(Resources.Store, isRequired: true, valueTypeHint: "IStorageStore or IStorageStoreFactory"),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            Types.Delete =>
            [
                ComponentResources.Metadata<IStorageStore>(Resources.Store, isRequired: true, valueTypeHint: "IStorageStore or IStorageStoreFactory"),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Put = "storage.put";
        public const string Get = "storage.get";
        public const string Query = "storage.query";
        public const string Delete = "storage.delete";
    }

    public static class Ports
    {
        public const string Input = "Input";
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Store = "store";
        public const string Clock = "clock";
    }
}
