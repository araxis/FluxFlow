using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.State.Composition;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.State.Designer;

public static class StateDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddStateDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(StateComponentDefinition.Types.Reducer, DescribeReducer);
    }

    private static ComponentDesignMetadata DescribeReducer(ComponentDesignMetadata metadata)
    {
        metadata = metadata.WithDisplay("State Reducer", "State", "Maintains keyed state by applying a reducer expression to each input message.", "database-zap", "stateReducer", 460);
        metadata = metadata
            .WithPort(StateComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "State reducer request.", true)
            .WithPort(StateComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "State reducer result.", true)
            .WithPort(StateComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort state diagnostics.");
        metadata = metadata.WithOption(StateComponentDefinition.Options.KeyExpression, OptionValueKind.Text, "Key Expression", "Optional expression used to resolve the state key from each input.", section: "State", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Expression, syntax: OptionDesignMetadataAttributeValues.Expression, relatedResource: StateComponentDefinition.Resources.Engine);
        metadata = metadata.WithOption(StateComponentDefinition.Options.Reducer, OptionValueKind.Text, "Reducer", "Expression evaluated once per reduce operation to produce the next state.", section: "State", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Expression, syntax: OptionDesignMetadataAttributeValues.Expression, relatedResource: StateComponentDefinition.Resources.Engine);
        metadata = metadata.WithOption(StateComponentDefinition.Options.ExpressionId, OptionValueKind.Text, "Expression ID", "Optional expression identifier emitted in diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(StateComponentDefinition.Options.ExpressionName, OptionValueKind.Text, "Expression Name", "Optional expression display name emitted in diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        metadata = metadata.WithOption(StateComponentDefinition.Options.InitialState, OptionValueKind.Json, "Initial State", "Optional initial state used for new keys or reset operations.", section: "State", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Json);
        metadata = metadata.WithOption(StateComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(StateComponentDefinition.Options.MaxKeys, OptionValueKind.Number, "Max Keys", "Maximum number of keys to track. Zero rejects new keys.", defaultValue: 1024, min: 0, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithResource(StateComponentDefinition.Resources.Engine, "Engine", 0, "Required keyed expression engine used to evaluate reducer and key expressions.", designValueType: nameof(IFlowExpressionEngine), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.ExpressionEngine, keyPattern: "Resources.{name}");
        metadata = metadata.WithResource(StateComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic state reducer results and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        return metadata;
    }
}
