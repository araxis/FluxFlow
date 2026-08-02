using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Serialization.Composition;

public static class SerializationServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddSerialization(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddComponent(SerializationComponentDefinition.Types.JsonParse, ConfigureJsonParse)
            .AddComponent(SerializationComponentDefinition.Types.JsonStringify, ConfigureJsonStringify)
            .AddComponent(SerializationComponentDefinition.Types.TextEncode, ConfigureTextEncode)
            .AddComponent(SerializationComponentDefinition.Types.TextDecode, ConfigureTextDecode)
            .AddComponent(SerializationComponentDefinition.Types.Base64Encode, ConfigureBase64Encode)
            .AddComponent(SerializationComponentDefinition.Types.Base64Decode, ConfigureBase64Decode);
    }

    private static void ConfigureJsonParse(ComponentRegistrationBuilder component)
        => Configure<FlowContent, JsonElement>(component, CreateJsonParseNode, "JSON Parse", "Parses JSON content into an independently owned JSON value.", "braces", "parse");

    private static void ConfigureJsonStringify(ComponentRegistrationBuilder component)
        => Configure<JsonElement, FlowContent>(component, CreateJsonStringifyNode, "JSON Stringify", "Serializes a JSON value into exact JSON content.", "file-json", "stringify");

    private static void ConfigureTextEncode(ComponentRegistrationBuilder component)
        => Configure<string, FlowContent>(component, CreateTextEncodeNode, "Text Encode", "Encodes a string into exact text content.", "binary", "encode");

    private static void ConfigureTextDecode(ComponentRegistrationBuilder component)
        => Configure<FlowContent, string>(component, CreateTextDecodeNode, "Text Decode", "Decodes text content into a string.", "letter-text", "decode");

    private static void ConfigureBase64Encode(ComponentRegistrationBuilder component)
        => Configure<FlowContent, string>(component, CreateBase64EncodeNode, "Base64 Encode", "Encodes exact content bytes into a Base64 string value.", "file-up", "base64Encode");

    private static void ConfigureBase64Decode(ComponentRegistrationBuilder component)
        => Configure<string, FlowContent>(component, CreateBase64DecodeNode, "Base64 Decode", "Decodes a Base64 string value into binary content.", "file-down", "base64Decode");

    private static void Configure<TInput, TOutput>(ComponentRegistrationBuilder component, ComponentFactory factory, string displayName, string summary, string iconKey, string preferredNodeName)
    {
        var defaults = new SerializationNodeOptions();
        component.UseFactory(factory);
        component.WithDisplay(displayName, "Serialization", summary, iconKey, preferredNodeName, 420);
        component.AddInput<TInput>(SerializationComponentDefinition.Ports.Input, "Input", "Messages", 0, "Canonical conversion input.", true);
        component.AddOutput<TOutput>(SerializationComponentDefinition.Ports.Output, "Output", "Results", 1, "Converted value; conversion failures use the message error case.", true);
        component.AddOption<int>(SerializationComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<string>(SerializationComponentDefinition.Options.DefaultEncoding, OptionValueKind.Text, "Default Encoding", "Encoding used when content does not declare one.", defaultValue: defaults.DefaultEncoding, section: "Encoding", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<int>(SerializationComponentDefinition.Options.MaxInputBytes, OptionValueKind.Number, "Max Input Bytes", "Maximum input payload size accepted by the node.", defaultValue: defaults.MaxInputBytes, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(SerializationComponentDefinition.Options.MaxOutputBytes, OptionValueKind.Number, "Max Output Bytes", "Maximum output payload size emitted by the node.", defaultValue: defaults.MaxOutputBytes, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<bool>(SerializationComponentDefinition.Options.WriteIndented, OptionValueKind.Boolean, "Write Indented", "Write formatted JSON where the node emits JSON text.", defaultValue: defaults.WriteIndented, section: "JSON", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<bool>(SerializationComponentDefinition.Options.AllowTrailingCommas, OptionValueKind.Boolean, "Allow Trailing Commas", "Allow trailing commas while parsing JSON.", defaultValue: defaults.AllowTrailingCommas, section: "JSON", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<bool>(SerializationComponentDefinition.Options.SkipComments, OptionValueKind.Boolean, "Skip Comments", "Skip comments while parsing JSON.", defaultValue: defaults.SkipComments, section: "JSON", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddResource<TimeProvider>(SerializationComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic serialization diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
    }

    private static ValueTask<ComponentInstance> CreateJsonParseNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new JsonParseNode(options, GetClock(context));
        return Compose<FlowContent, JsonElement>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateJsonStringifyNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new JsonStringifyNode(options, GetClock(context));
        return Compose<JsonElement, FlowContent>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateTextEncodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new TextEncodeNode(options, GetClock(context));
        return Compose<string, FlowContent>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateTextDecodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new TextDecodeNode(options, GetClock(context));
        return Compose<FlowContent, string>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateBase64EncodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new Base64EncodeNode(options, GetClock(context));
        return Compose<FlowContent, string>(node, node.Input, node.Output, node.Events);
    }

    private static ValueTask<ComponentInstance> CreateBase64DecodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        var node = new Base64DecodeNode(options, GetClock(context));
        return Compose<string, FlowContent>(node, node.Input, node.Output, node.Events);
    }

    private static TimeProvider? GetClock(ComponentActivationContext context)
        => context.GetResource<TimeProvider>(SerializationComponentDefinition.Resources.Clock);

    private static ValueTask<ComponentInstance> Compose<TInput, TOutput>(
        IFlowNode node,
        ITargetBlock<FlowMessage<TInput>> input,
        ISourceBlock<FlowMessage<TOutput>> output,
        ISourceBlock<FlowEvent> events)
    {
        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<TInput>(
                    SerializationComponentDefinition.Ports.Input,
                    input)
            ],
            outputs:
            [
                ComponentPorts.Output<TOutput>(
                    SerializationComponentDefinition.Ports.Output,
                    output)
            ],
            events: events));
    }
}
