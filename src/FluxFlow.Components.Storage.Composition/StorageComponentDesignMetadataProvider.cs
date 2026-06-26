using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Options;

namespace FluxFlow.Components.Storage.Composition;

public sealed class StorageComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly StoragePutOptions PutDefaults = new();
    private static readonly StorageGetOptions GetDefaults = new();
    private static readonly StorageQueryOptions QueryDefaults = new();
    private static readonly StorageDeleteOptions DeleteDefaults = new();

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
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
            StorageCompositionNodeTypes.Put,
            "Storage Put",
            "Stores or updates a record through a host-owned storage store.",
            "database-plus",
            "putRecord");

        builder
            .AddOption(CollectionOption())
            .AddOption(
                "mode",
                OptionValueKind.Enum,
                displayName: "Mode",
                helperText: "Write behavior when a record already exists or is missing.",
                defaultValue: PutDefaults.Mode.ToString(),
                choices: WriteModeChoices())
            .AddOption(
                "emitStoredRecord",
                OptionValueKind.Boolean,
                displayName: "Emit Stored Record",
                helperText: "Include the stored record in the output result.",
                defaultValue: PutDefaults.EmitStoredRecord)
            .AddOption(BoundedCapacityOption(PutDefaults.BoundedCapacity));

        AddTransformPorts(
            builder,
            nameof(StoragePutRequest),
            "Storage put request.",
            nameof(StorageResult),
            "Storage put result.");

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateGetMetadata()
    {
        var builder = CreateStorageMetadataBuilder(
            StorageCompositionNodeTypes.Get,
            "Storage Get",
            "Reads a record and routes found or missing results.",
            "database-search",
            "getRecord");

        builder
            .AddOption(CollectionOption())
            .AddOption(IncludeExpiredOption(GetDefaults.IncludeExpired))
            .AddOption(BoundedCapacityOption(GetDefaults.BoundedCapacity));

        builder
            .AddInputPort(
                StorageCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Storage get request.",
                valueType: nameof(StorageGetRequest),
                isPrimary: true)
            .AddOutputPort(
                StorageCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Storage get result.",
                valueType: nameof(StorageResult),
                isPrimary: true)
            .AddOutputPort(
                StorageCompositionPortNames.Found,
                displayName: "Found",
                group: "Branches",
                order: 2,
                summary: "Storage result when the record exists.",
                valueType: nameof(StorageResult))
            .AddOutputPort(
                StorageCompositionPortNames.NotFound,
                displayName: "Not Found",
                group: "Branches",
                order: 3,
                summary: "Storage result when the record is missing.",
                valueType: nameof(StorageResult));

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateQueryMetadata()
    {
        var builder = CreateStorageMetadataBuilder(
            StorageCompositionNodeTypes.Query,
            "Storage Query",
            "Queries records and can fan matched records to a separate output.",
            "database",
            "queryRecords");

        builder
            .AddOption(CollectionOption())
            .AddOption(IncludeExpiredOption(QueryDefaults.IncludeExpired))
            .AddOption(
                "offset",
                OptionValueKind.Number,
                displayName: "Offset",
                helperText: "Number of matched records to skip.",
                defaultValue: QueryDefaults.Offset,
                min: 0)
            .AddOption(
                "limit",
                OptionValueKind.Number,
                displayName: "Limit",
                helperText: "Maximum number of records to return.",
                defaultValue: QueryDefaults.Limit,
                min: 1)
            .AddOption(
                "emitRecordsInResult",
                OptionValueKind.Boolean,
                displayName: "Emit Records In Result",
                helperText: "Include matched records in the query result payload.",
                defaultValue: QueryDefaults.EmitRecordsInResult)
            .AddOption(
                "emitRecordOutputs",
                OptionValueKind.Boolean,
                displayName: "Emit Record Outputs",
                helperText: "Fan each matched record to the Records output.",
                defaultValue: QueryDefaults.EmitRecordOutputs)
            .AddOption(BoundedCapacityOption(QueryDefaults.BoundedCapacity));

        builder
            .AddInputPort(
                StorageCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Storage query request.",
                valueType: nameof(StorageQueryRequest),
                isPrimary: true)
            .AddOutputPort(
                StorageCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Storage query result.",
                valueType: nameof(StorageQueryResult),
                isPrimary: true)
            .AddOutputPort(
                StorageCompositionPortNames.Records,
                displayName: "Records",
                group: "Records",
                order: 2,
                summary: "Matched storage record.",
                valueType: nameof(StorageRecord));

        return builder.Build();
    }

    private static ComponentDesignMetadata CreateDeleteMetadata()
    {
        var builder = CreateStorageMetadataBuilder(
            StorageCompositionNodeTypes.Delete,
            "Storage Delete",
            "Deletes a record through a host-owned storage store.",
            "database-x",
            "deleteRecord");

        builder
            .AddOption(CollectionOption())
            .AddOption(
                "emitMissingAsResult",
                OptionValueKind.Boolean,
                displayName: "Emit Missing As Result",
                helperText: "Emit a normal output result when the record is missing.",
                defaultValue: DeleteDefaults.EmitMissingAsResult)
            .AddOption(BoundedCapacityOption(DeleteDefaults.BoundedCapacity));

        AddTransformPorts(
            builder,
            nameof(StorageDeleteRequest),
            "Storage delete request.",
            nameof(StorageResult),
            "Storage delete result.");

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
            .AddResource(
                StorageCompositionResourceNames.Store,
                displayName: "Store",
                order: 0,
                summary: "Required keyed storage store or store factory used for put, get, query, and delete operations.",
                valueType: $"{nameof(IStorageStore)} or {nameof(IStorageStoreFactory)}",
                isRequired: true)
            .AddResource(
                StorageCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic storage diagnostics and timestamps.",
                valueType: nameof(TimeProvider));

    private static OptionDesignMetadata CollectionOption() => new()
    {
        Name = "collection",
        Kind = OptionValueKind.Text,
        DisplayName = "Collection",
        HelperText = "Default collection used when the input request does not specify one."
    };

    private static OptionDesignMetadata IncludeExpiredOption(bool defaultValue) => new()
    {
        Name = "includeExpired",
        Kind = OptionValueKind.Boolean,
        DisplayName = "Include Expired",
        DefaultValue = defaultValue,
        HelperText = "Include records that the store considers expired."
    };

    private static OptionDesignMetadata BoundedCapacityOption(int defaultValue) => new()
    {
        Name = "boundedCapacity",
        Kind = OptionValueKind.Number,
        DisplayName = "Bounded Capacity",
        DefaultValue = defaultValue,
        Min = 1,
        HelperText = "Maximum queued input messages."
    };

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
            Value = mode.ToString(),
            DisplayName = displayName,
            HelperText = helperText
        };

    private static void AddTransformPorts(
        ComponentDesignMetadataBuilder builder,
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        => builder
            .AddInputPort(
                StorageCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: inputSummary,
                valueType: inputType,
                isPrimary: true)
            .AddOutputPort(
                StorageCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: outputSummary,
                valueType: outputType,
                isPrimary: true);
}
