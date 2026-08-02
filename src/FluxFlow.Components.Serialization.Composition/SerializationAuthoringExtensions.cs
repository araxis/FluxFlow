using System.Text.Json;
using FluxFlow.Composition.Authoring;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Composition;

public static class SerializationAuthoringExtensions
{
    public static InputOutputComponentHandle<FlowContent, JsonElement> AddJsonParse(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<SerializationComponentBuilder>? configure = null)
        => Add<FlowContent, JsonElement>(workflow, name, SerializationComponentDefinition.Types.JsonParse, configure);

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
        => Add<JsonElement, FlowContent>(workflow, name, SerializationComponentDefinition.Types.JsonStringify, configure);

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
        => Add<string, FlowContent>(workflow, name, SerializationComponentDefinition.Types.TextEncode, configure);

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
        => Add<FlowContent, string>(workflow, name, SerializationComponentDefinition.Types.TextDecode, configure);

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
        => Add<FlowContent, string>(workflow, name, SerializationComponentDefinition.Types.Base64Encode, configure);

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
        => Add<string, FlowContent>(workflow, name, SerializationComponentDefinition.Types.Base64Decode, configure);

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

    private static InputOutputComponentHandle<TInput, TOutput> Add<TInput, TOutput>(
        WorkflowDefinitionBuilder workflow,
        string name,
        string type,
        Action<SerializationComponentBuilder>? configure)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var component = workflow.AddComponent(name, type, definition =>
        {
            var builder = new SerializationComponentBuilder();
            configure?.Invoke(builder);
            builder.Apply(definition);
        });
        return new(component, SerializationComponentDefinition.Ports.Input, SerializationComponentDefinition.Ports.Output);
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
