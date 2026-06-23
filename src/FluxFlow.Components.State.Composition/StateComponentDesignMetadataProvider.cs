using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.State.Contracts;

namespace FluxFlow.Components.State.Composition;

public sealed class StateComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private const int DefaultBoundedCapacity = 128;
    private const int DefaultMaxKeys = 1024;

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreateReducerMetadata()];

    private static ComponentDesignMetadata CreateReducerMetadata() => new()
    {
        Type = new ComponentType(StateCompositionNodeTypes.Reducer),
        DisplayName = "State Reducer",
        Category = "State",
        Summary = "Maintains keyed state by applying a reducer expression to each input message.",
        IconKey = "database-zap",
        PreferredNodeName = "stateReducer",
        SuggestedEditorWidth = 460,
        Options =
        [
            new OptionDesignMetadata
            {
                Name = "engine",
                Kind = OptionValueKind.Text,
                DisplayName = "Engine",
                HelperText = "Diagnostic engine metadata; DI selection uses the required host-owned engine resource."
            },
            new OptionDesignMetadata
            {
                Name = "keyExpression",
                Kind = OptionValueKind.Text,
                DisplayName = "Key Expression",
                HelperText = "Optional expression used to resolve the state key from each input."
            },
            new OptionDesignMetadata
            {
                Name = "reducer",
                Kind = OptionValueKind.Text,
                DisplayName = "Reducer",
                HelperText = "Expression evaluated once per reduce operation to produce the next state.",
                IsRequired = true
            },
            new OptionDesignMetadata
            {
                Name = "expressionId",
                Kind = OptionValueKind.Text,
                DisplayName = "Expression ID",
                HelperText = "Optional expression identifier emitted in diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "expressionName",
                Kind = OptionValueKind.Text,
                DisplayName = "Expression Name",
                HelperText = "Optional expression display name emitted in diagnostics."
            },
            new OptionDesignMetadata
            {
                Name = "initialState",
                Kind = OptionValueKind.Json,
                DisplayName = "Initial State",
                HelperText = "Optional initial state used for new keys or reset operations."
            },
            new OptionDesignMetadata
            {
                Name = "boundedCapacity",
                Kind = OptionValueKind.Number,
                DisplayName = "Bounded Capacity",
                DefaultValue = DefaultBoundedCapacity,
                Min = 1,
                HelperText = "Maximum queued input messages."
            },
            new OptionDesignMetadata
            {
                Name = "maxKeys",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Keys",
                DefaultValue = DefaultMaxKeys,
                Min = 0,
                HelperText = "Maximum number of keys to track. Zero rejects new keys."
            }
        ],
        Ports =
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(StateCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "State reducer request.",
                ValueType = nameof(StateReducerInput),
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(StateCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = "State reducer result.",
                ValueType = nameof(StateReducerResult),
                IsPrimary = true
            }
        ]
    };
}
