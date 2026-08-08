using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Composition;

public static class SerializationComponents
{
    public static ComponentContract<SerializationComponentBuilder, InputOutputComponentHandle<FlowContent, JsonElement>> JsonParse { get; } =
        Create<FlowContent, JsonElement>(SerializationComponentDefinition.Types.JsonParse, SerializationServiceCollectionExtensions.ConfigureJsonParse);

    public static ComponentContract<SerializationComponentBuilder, InputOutputComponentHandle<JsonElement, FlowContent>> JsonStringify { get; } =
        Create<JsonElement, FlowContent>(SerializationComponentDefinition.Types.JsonStringify, SerializationServiceCollectionExtensions.ConfigureJsonStringify);

    public static ComponentContract<SerializationComponentBuilder, InputOutputComponentHandle<string, FlowContent>> TextEncode { get; } =
        Create<string, FlowContent>(SerializationComponentDefinition.Types.TextEncode, SerializationServiceCollectionExtensions.ConfigureTextEncode);

    public static ComponentContract<SerializationComponentBuilder, InputOutputComponentHandle<FlowContent, string>> TextDecode { get; } =
        Create<FlowContent, string>(SerializationComponentDefinition.Types.TextDecode, SerializationServiceCollectionExtensions.ConfigureTextDecode);

    public static ComponentContract<SerializationComponentBuilder, InputOutputComponentHandle<FlowContent, string>> Base64Encode { get; } =
        Create<FlowContent, string>(SerializationComponentDefinition.Types.Base64Encode, SerializationServiceCollectionExtensions.ConfigureBase64Encode);

    public static ComponentContract<SerializationComponentBuilder, InputOutputComponentHandle<string, FlowContent>> Base64Decode { get; } =
        Create<string, FlowContent>(SerializationComponentDefinition.Types.Base64Decode, SerializationServiceCollectionExtensions.ConfigureBase64Decode);

    private static ComponentContract<SerializationComponentBuilder, InputOutputComponentHandle<TInput, TOutput>> Create<TInput, TOutput>(
        string type,
        Action<ComponentRegistrationBuilder> configure)
        => DesignedComponentContract.Create(
            type,
            configure,
            static () => new SerializationComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<TInput, TOutput>(component, SerializationComponentDefinition.Ports.Input, SerializationComponentDefinition.Ports.Output, SerializationComponentDefinition.Ports.Events));
}

public static class SerializationAuthoringExtensions
{
    public static InputOutputComponentHandle<FlowContent, JsonElement> AddJsonParse(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder>? configure = null)
        => workflow.AddComponent(name, SerializationComponents.JsonParse, configure ?? (static _ => { }));

    public static WorkflowDefinitionBuilder AddJsonParse(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<FlowContent, JsonElement> parser)
    {
        parser = workflow.AddJsonParse(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddJsonParse(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder> configure,
        out InputOutputComponentHandle<FlowContent, JsonElement> parser)
    {
        ArgumentNullException.ThrowIfNull(configure);
        parser = workflow.AddJsonParse(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<JsonElement, FlowContent> AddJsonStringify(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder>? configure = null)
        => workflow.AddComponent(name, SerializationComponents.JsonStringify, configure ?? (static _ => { }));

    public static WorkflowDefinitionBuilder AddJsonStringify(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<JsonElement, FlowContent> stringify)
    {
        stringify = workflow.AddJsonStringify(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddJsonStringify(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, FlowContent> stringify)
    {
        ArgumentNullException.ThrowIfNull(configure);
        stringify = workflow.AddJsonStringify(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<string, FlowContent> AddTextEncode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder>? configure = null)
        => workflow.AddComponent(name, SerializationComponents.TextEncode, configure ?? (static _ => { }));

    public static WorkflowDefinitionBuilder AddTextEncode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<string, FlowContent> encoder)
    {
        encoder = workflow.AddTextEncode(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddTextEncode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder> configure,
        out InputOutputComponentHandle<string, FlowContent> encoder)
    {
        ArgumentNullException.ThrowIfNull(configure);
        encoder = workflow.AddTextEncode(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FlowContent, string> AddTextDecode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder>? configure = null)
        => workflow.AddComponent(name, SerializationComponents.TextDecode, configure ?? (static _ => { }));

    public static WorkflowDefinitionBuilder AddTextDecode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<FlowContent, string> decoder)
    {
        decoder = workflow.AddTextDecode(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddTextDecode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder> configure,
        out InputOutputComponentHandle<FlowContent, string> decoder)
    {
        ArgumentNullException.ThrowIfNull(configure);
        decoder = workflow.AddTextDecode(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<FlowContent, string> AddBase64Encode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder>? configure = null)
        => workflow.AddComponent(name, SerializationComponents.Base64Encode, configure ?? (static _ => { }));

    public static WorkflowDefinitionBuilder AddBase64Encode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<FlowContent, string> encoder)
    {
        encoder = workflow.AddBase64Encode(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddBase64Encode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder> configure,
        out InputOutputComponentHandle<FlowContent, string> encoder)
    {
        ArgumentNullException.ThrowIfNull(configure);
        encoder = workflow.AddBase64Encode(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<string, FlowContent> AddBase64Decode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder>? configure = null)
        => workflow.AddComponent(name, SerializationComponents.Base64Decode, configure ?? (static _ => { }));

    public static WorkflowDefinitionBuilder AddBase64Decode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<string, FlowContent> decoder)
    {
        decoder = workflow.AddBase64Decode(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddBase64Decode(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder> configure,
        out InputOutputComponentHandle<string, FlowContent> decoder)
    {
        ArgumentNullException.ThrowIfNull(configure);
        decoder = workflow.AddBase64Decode(name, configure);
        return workflow;
    }

}

public sealed class SerializationComponentBuilder
{
    public int? BoundedCapacity { get; set; }
    public string? DefaultEncoding { get; set; }
    public int? MaxInputBytes { get; set; }
    public int? MaxOutputBytes { get; set; }
    public bool? WriteIndented { get; set; }
    public bool? AllowTrailingCommas { get; set; }
    public bool? SkipComments { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        Set(definition, SerializationComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        Set(definition, SerializationComponentDefinition.Options.DefaultEncoding, DefaultEncoding);
        Set(definition, SerializationComponentDefinition.Options.MaxInputBytes, MaxInputBytes);
        Set(definition, SerializationComponentDefinition.Options.MaxOutputBytes, MaxOutputBytes);
        Set(definition, SerializationComponentDefinition.Options.WriteIndented, WriteIndented);
        Set(definition, SerializationComponentDefinition.Options.AllowTrailingCommas, AllowTrailingCommas);
        Set(definition, SerializationComponentDefinition.Options.SkipComments, SkipComments);
        if (Clock is not null)
            definition.UseResource(SerializationComponentDefinition.Resources.Clock, Clock);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}
