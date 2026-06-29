using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Mapping;

namespace FluxFlow.Components.State.Composition;

public sealed class StateComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private const int DefaultBoundedCapacity = 128;
    private const int DefaultMaxKeys = 1024;

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateReducerMetadata()];

    private static ComponentDesignMetadata CreateReducerMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(StateCompositionNodeTypes.Reducer)
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
                "engine",
                OptionValueKind.Text,
                displayName: "Engine",
                helperText: "Diagnostic engine metadata; DI selection uses the required host-owned engine resource.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "keyExpression",
                OptionValueKind.Text,
                displayName: "Key Expression",
                helperText: "Optional expression used to resolve the state key from each input.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "State",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: StateCompositionResourceNames.Engine))
            .AddOption(
                "reducer",
                OptionValueKind.Text,
                displayName: "Reducer",
                helperText: "Expression evaluated once per reduce operation to produce the next state.",
                isRequired: true,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "State",
                    importance: OptionDesignMetadataAttributeValues.Primary,
                    editor: OptionDesignMetadataAttributeValues.Expression,
                    syntax: OptionDesignMetadataAttributeValues.Expression,
                    relatedResource: StateCompositionResourceNames.Engine))
            .AddOption(
                "expressionId",
                OptionValueKind.Text,
                displayName: "Expression ID",
                helperText: "Optional expression identifier emitted in diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "expressionName",
                OptionValueKind.Text,
                displayName: "Expression Name",
                helperText: "Optional expression display name emitted in diagnostics.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Diagnostics",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Text))
            .AddOption(
                "initialState",
                OptionValueKind.Json,
                displayName: "Initial State",
                helperText: "Optional initial state used for new keys or reset operations.",
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "State",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Json))
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: DefaultBoundedCapacity,
                min: 1,
                attributes: OptionDesignMetadataAttributes.Create(
                    section: "Runtime",
                    importance: OptionDesignMetadataAttributeValues.Advanced,
                    editor: OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                "maxKeys",
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
                StateCompositionResourceNames.Engine,
                displayName: "Engine",
                order: 0,
                summary: "Required keyed expression engine used to evaluate reducer and key expressions.",
                valueType: nameof(IFlowExpressionEngine),
                isRequired: true,
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.ExpressionEngine,
                    keyPattern: "expression-engine:{name}"))
            .AddResource(
                StateCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic state reducer results and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "clock:{name}"));

    private static void AddReducerPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                StateCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "State reducer request.",
                valueType: nameof(StateReducerInput),
                isPrimary: true)
            .AddOutputPort(
                StateCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "State reducer result.",
                valueType: nameof(StateReducerResult),
                isPrimary: true);
}
