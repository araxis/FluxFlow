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

    private static ComponentDesignMetadata CreatePutMetadata() => new()
    {
        Type = new ComponentType(StorageCompositionNodeTypes.Put),
        DisplayName = "Storage Put",
        Category = "Storage",
        Summary = "Stores or updates a record through a host-owned storage store.",
        IconKey = "database-plus",
        PreferredNodeName = "putRecord",
        SuggestedEditorWidth = 460,
        Options =
        [
            CollectionOption(),
            new OptionDesignMetadata
            {
                Name = "mode",
                Kind = OptionValueKind.Enum,
                DisplayName = "Mode",
                DefaultValue = PutDefaults.Mode.ToString(),
                HelperText = "Write behavior when a record already exists or is missing.",
                Choices = WriteModeChoices()
            },
            new OptionDesignMetadata
            {
                Name = "emitStoredRecord",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Emit Stored Record",
                DefaultValue = PutDefaults.EmitStoredRecord,
                HelperText = "Include the stored record in the output result."
            },
            BoundedCapacityOption(PutDefaults.BoundedCapacity)
        ],
        Ports = TransformPorts(
            nameof(StoragePutRequest),
            "Storage put request.",
            nameof(StorageResult),
            "Storage put result.")
    };

    private static ComponentDesignMetadata CreateGetMetadata() => new()
    {
        Type = new ComponentType(StorageCompositionNodeTypes.Get),
        DisplayName = "Storage Get",
        Category = "Storage",
        Summary = "Reads a record and routes found or missing results.",
        IconKey = "database-search",
        PreferredNodeName = "getRecord",
        SuggestedEditorWidth = 460,
        Options =
        [
            CollectionOption(),
            IncludeExpiredOption(GetDefaults.IncludeExpired),
            BoundedCapacityOption(GetDefaults.BoundedCapacity)
        ],
        Ports =
        [
            InputPort(nameof(StorageGetRequest), "Storage get request."),
            OutputPort(
                StorageCompositionPortNames.Output,
                "Output",
                "Results",
                1,
                nameof(StorageResult),
                "Storage get result.",
                isPrimary: true),
            OutputPort(
                StorageCompositionPortNames.Found,
                "Found",
                "Branches",
                2,
                nameof(StorageResult),
                "Storage result when the record exists."),
            OutputPort(
                StorageCompositionPortNames.NotFound,
                "Not Found",
                "Branches",
                3,
                nameof(StorageResult),
                "Storage result when the record is missing.")
        ]
    };

    private static ComponentDesignMetadata CreateQueryMetadata() => new()
    {
        Type = new ComponentType(StorageCompositionNodeTypes.Query),
        DisplayName = "Storage Query",
        Category = "Storage",
        Summary = "Queries records and can fan matched records to a separate output.",
        IconKey = "database",
        PreferredNodeName = "queryRecords",
        SuggestedEditorWidth = 460,
        Options =
        [
            CollectionOption(),
            IncludeExpiredOption(QueryDefaults.IncludeExpired),
            new OptionDesignMetadata
            {
                Name = "offset",
                Kind = OptionValueKind.Number,
                DisplayName = "Offset",
                DefaultValue = QueryDefaults.Offset,
                Min = 0,
                HelperText = "Number of matched records to skip."
            },
            new OptionDesignMetadata
            {
                Name = "limit",
                Kind = OptionValueKind.Number,
                DisplayName = "Limit",
                DefaultValue = QueryDefaults.Limit,
                Min = 1,
                HelperText = "Maximum number of records to return."
            },
            new OptionDesignMetadata
            {
                Name = "emitRecordsInResult",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Emit Records In Result",
                DefaultValue = QueryDefaults.EmitRecordsInResult,
                HelperText = "Include matched records in the query result payload."
            },
            new OptionDesignMetadata
            {
                Name = "emitRecordOutputs",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Emit Record Outputs",
                DefaultValue = QueryDefaults.EmitRecordOutputs,
                HelperText = "Fan each matched record to the Records output."
            },
            BoundedCapacityOption(QueryDefaults.BoundedCapacity)
        ],
        Ports =
        [
            InputPort(nameof(StorageQueryRequest), "Storage query request."),
            OutputPort(
                StorageCompositionPortNames.Output,
                "Output",
                "Results",
                1,
                nameof(StorageQueryResult),
                "Storage query result.",
                isPrimary: true),
            OutputPort(
                StorageCompositionPortNames.Records,
                "Records",
                "Records",
                2,
                nameof(StorageRecord),
                "Matched storage record.")
        ]
    };

    private static ComponentDesignMetadata CreateDeleteMetadata() => new()
    {
        Type = new ComponentType(StorageCompositionNodeTypes.Delete),
        DisplayName = "Storage Delete",
        Category = "Storage",
        Summary = "Deletes a record through a host-owned storage store.",
        IconKey = "database-x",
        PreferredNodeName = "deleteRecord",
        SuggestedEditorWidth = 460,
        Options =
        [
            CollectionOption(),
            new OptionDesignMetadata
            {
                Name = "emitMissingAsResult",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Emit Missing As Result",
                DefaultValue = DeleteDefaults.EmitMissingAsResult,
                HelperText = "Emit a normal output result when the record is missing."
            },
            BoundedCapacityOption(DeleteDefaults.BoundedCapacity)
        ],
        Ports = TransformPorts(
            nameof(StorageDeleteRequest),
            "Storage delete request.",
            nameof(StorageResult),
            "Storage delete result.")
    };

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

    private static IReadOnlyList<PortDesignMetadata> TransformPorts(
        string inputType,
        string inputSummary,
        string outputType,
        string outputSummary)
        =>
        [
            InputPort(inputType, inputSummary),
            OutputPort(
                StorageCompositionPortNames.Output,
                "Output",
                "Results",
                1,
                outputType,
                outputSummary,
                isPrimary: true)
        ];

    private static PortDesignMetadata InputPort(
        string valueType,
        string summary) => new()
        {
            Name = new ComponentPortName(StorageCompositionPortNames.Input),
            Direction = PortDirection.Input,
            DisplayName = "Input",
            Group = "Messages",
            Order = 0,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = true
        };

    private static PortDesignMetadata OutputPort(
        string name,
        string displayName,
        string group,
        int order,
        string valueType,
        string summary,
        bool isPrimary = false) => new()
        {
            Name = new ComponentPortName(name),
            Direction = PortDirection.Output,
            DisplayName = displayName,
            Group = group,
            Order = order,
            Summary = summary,
            ValueType = valueType,
            IsPrimary = isPrimary
        };
}
