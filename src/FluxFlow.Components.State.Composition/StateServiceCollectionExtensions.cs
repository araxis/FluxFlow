using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Nodes;
using FluxFlow.Components.State.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.State.Composition;

public static class StateServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddState(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(StateComponentDefinition.Types.Reducer, ConfigureReducer);
    }

    private static void ConfigureReducer(ComponentRegistrationBuilder component)
    {
        component.UseFactory(CreateStateReducerNode);
        component.WithDisplay("State Reducer", "State", "Maintains keyed state by applying a reducer expression to each input message.", "database-zap", "stateReducer", 460);
        component.AddInput<StateReducerInput<JsonElement>>(StateComponentDefinition.Ports.Input, "Input", "Messages", 0, "State reducer request.", true);
        component.AddOutput<StateReducerResult<JsonElement>>(StateComponentDefinition.Ports.Output, "Output", "Results", 1, "State reducer result.", true);
        component.AddOption<string>(StateComponentDefinition.Options.KeyExpression, OptionValueKind.Text, "Key Expression", "Optional expression used to resolve the state key from each input.", section: "State", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Expression, syntax: OptionDesignMetadataAttributeValues.Expression, relatedResource: StateComponentDefinition.Resources.Engine);
        component.AddOption<string>(StateComponentDefinition.Options.Reducer, OptionValueKind.Text, "Reducer", "Expression evaluated once per reduce operation to produce the next state.", true, section: "State", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Expression, syntax: OptionDesignMetadataAttributeValues.Expression, relatedResource: StateComponentDefinition.Resources.Engine);
        component.AddOption<string>(StateComponentDefinition.Options.ExpressionId, OptionValueKind.Text, "Expression ID", "Optional expression identifier emitted in diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<string>(StateComponentDefinition.Options.ExpressionName, OptionValueKind.Text, "Expression Name", "Optional expression display name emitted in diagnostics.", section: "Diagnostics", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<JsonElement?>(StateComponentDefinition.Options.InitialState, OptionValueKind.Json, "Initial State", "Optional initial state used for new keys or reset operations.", section: "State", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Json);
        component.AddOption<int>(StateComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: 128, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(StateComponentDefinition.Options.MaxKeys, OptionValueKind.Number, "Max Keys", "Maximum number of keys to track. Zero rejects new keys.", defaultValue: 1024, min: 0, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddResource<IFlowExpressionEngine>(StateComponentDefinition.Resources.Engine, "Engine", 0, "Required keyed expression engine used to evaluate reducer and key expressions.", true, nameof(IFlowExpressionEngine), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.ExpressionEngine, keyPattern: "Resources.{name}");
        component.AddResource<TimeProvider>(StateComponentDefinition.Resources.Clock, "Clock", 1, "Optional keyed clock for deterministic state reducer results and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
    }

    private static ValueTask<ComponentInstance> CreateStateReducerNode(
        ComponentActivationContext context)
    {
        var configuration = context.BindConfiguration<StateReducerConfiguration>();
        var options = new StateReducerOptions<JsonElement>
        {
            KeyExpression = configuration.KeyExpression,
            Reducer = configuration.Reducer,
            ExpressionId = configuration.ExpressionId,
            ExpressionName = configuration.ExpressionName,
            InitialState = DecodeInitialState(configuration.InitialState),
            BoundedCapacity = configuration.BoundedCapacity,
            MaxKeys = configuration.MaxKeys
        };
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            StateComponentDefinition.Resources.Engine);
        var clock = context.GetResource<TimeProvider>(
            StateComponentDefinition.Resources.Clock);
        var node = new JsonStateReducerNode(options, expressionEngine, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<StateReducerInput<JsonElement>>(
                    StateComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<StateReducerResult<JsonElement>>(
                    StateComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static JsonElement DecodeInitialState(JsonElement? value)
        => value?.Clone() ?? JsonSerializer.SerializeToElement<object?>(null);
}
