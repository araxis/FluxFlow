using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Mapping;

namespace FluxFlow.Components.State.Composition;

public static partial class StateComponentDefinition
{
    private const int DefaultBoundedCapacity = 128;
    private const int DefaultMaxKeys = 1024;

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreateReducerMetadata()];

    private static ComponentDesignMetadata CreateReducerMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(StateComponentDefinition.Types.Reducer)
            .WithDisplay(
                displayName: "State Reducer",
                category: "State",
                summary: "Maintains keyed state by applying a reducer expression to each input message.",
                iconKey: "database-zap",
                preferredNodeName: "stateReducer",
                suggestedEditorWidth: 460);

        AddReducerOptions(builder);
        AddReducerResources(builder);
        AddReducerPorts(builder);

        return builder.Build();
    }

    private static void AddReducerOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                Options.KeyExpression,
                OptionValueKind.Text,
                displayName: "Key Expression",
                helperText: "Optional expression used to resolve the state key from each input.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "State",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: StateComponentDefinition.Resources.Engine))
            .AddOption(
                Options.Reducer,
                OptionValueKind.Text,
                displayName: "Reducer",
                helperText: "Expression evaluated once per reduce operation to produce the next state.",
                isRequired: true,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "State",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: StateComponentDefinition.Resources.Engine))
            .AddOption(
                Options.ExpressionId,
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional expression identifier emitted in diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.ExpressionName,
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional expression display name emitted in diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                Options.InitialState,
                OptionValueKind.Json,
                displayName: "Initial State",
                helperText: "Optional initial state used for new keys or reset operations.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "State",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Json))
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(DefaultBoundedCapacity))
            .AddOption(
                Options.MaxKeys,
                OptionValueKind.Number,
                displayName: "Max Keys",
                helperText: "Maximum number of keys to track. Zero rejects new keys.",
                defaultValue: DefaultMaxKeys,
                min: 0,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Runtime",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Number));

    private static void AddReducerResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                StateComponentDefinition.Resources.Engine,
                displayName: "Engine",
                order: 0,
                summary: "Required keyed expression engine used to evaluate reducer and key expressions.",
                valueType: nameof(IFlowExpressionEngine),
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ExpressionEngine,
                    keyPattern: "Resources.{name}"))
            .AddResource(
                StateComponentDefinition.Resources.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic state reducer results and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"));

    private static void AddReducerPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                StateComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "State reducer request.",
                valueType: "StateReducerInput<JsonElement>",
                isPrimary: true)
            .AddOutputPort(
                StateComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "State reducer result.",
                valueType: "StateReducerResult<JsonElement>",
                isPrimary: true);


    public static class Options
    {
        public const string KeyExpression = "keyExpression";
        public const string Reducer = "reducer";
        public const string ExpressionId = "expressionId";
        public const string ExpressionName = "expressionName";
        public const string InitialState = "initialState";
        public const string BoundedCapacity = "boundedCapacity";
        public const string MaxKeys = "maxKeys";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Reducer =>
            [
                ComponentOptions.Metadata<string>(Options.KeyExpression),
                ComponentOptions.Metadata<string>(Options.Reducer, isRequired: true),
                ComponentOptions.Metadata<string>(Options.ExpressionId),
                ComponentOptions.Metadata<string>(Options.ExpressionName),
                ComponentOptions.Metadata<JsonElement?>(Options.InitialState),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity),
                ComponentOptions.Metadata<int>(Options.MaxKeys)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Reducer =>
            [
                ComponentResources.Metadata<IFlowExpressionEngine>(Resources.Engine, isRequired: true),
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Reducer = "state.reduce";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Engine = "engine";
    
        public const string Clock = "clock";
    }
}
