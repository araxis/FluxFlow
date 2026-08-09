using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Serialization.Nodes;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Serialization.Composition;

public static class SerializationServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddSerialization(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignedComponent(SerializationComponents.JsonParse)
            .AddDesignedComponent(SerializationComponents.JsonStringify)
            .AddDesignedComponent(SerializationComponents.TextEncode)
            .AddDesignedComponent(SerializationComponents.TextDecode)
            .AddDesignedComponent(SerializationComponents.Base64Encode)
            .AddDesignedComponent(SerializationComponents.Base64Decode);
    }

    internal static void ConfigureJsonParse(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "JSON Parse", "Parses JSON content into an independently owned JSON value.", "braces", "parse");
        component.UseFactory(CreateJsonParseNode)
            .HasInput(SerializationComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Canonical conversion input.", true)
            .HasOutput(SerializationComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Converted value; conversion failures use the message error case.", true)
            .HasEvents(SerializationComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort serialization diagnostics.");
    }

    internal static void ConfigureJsonStringify(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "JSON Stringify", "Serializes a JSON value into exact JSON content.", "file-json", "stringify");
        component.UseFactory(CreateJsonStringifyNode)
            .HasInput(SerializationComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Canonical conversion input.", true)
            .HasOutput(SerializationComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Converted value; conversion failures use the message error case.", true)
            .HasEvents(SerializationComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort serialization diagnostics.");
    }

    internal static void ConfigureTextEncode(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Text Encode", "Encodes a string into exact text content.", "binary", "encode");
        component.UseFactory(CreateTextEncodeNode)
            .HasInput(SerializationComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Canonical conversion input.", true)
            .HasOutput(SerializationComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Converted value; conversion failures use the message error case.", true)
            .HasEvents(SerializationComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort serialization diagnostics.");
    }

    internal static void ConfigureTextDecode(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Text Decode", "Decodes text content into a string.", "letter-text", "decode");
        component.UseFactory(CreateTextDecodeNode)
            .HasInput(SerializationComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Canonical conversion input.", true)
            .HasOutput(SerializationComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Converted value; conversion failures use the message error case.", true)
            .HasEvents(SerializationComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort serialization diagnostics.");
    }

    internal static void ConfigureBase64Encode(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Base64 Encode", "Encodes exact content bytes into a Base64 string value.", "file-up", "base64Encode");
        component.UseFactory(CreateBase64EncodeNode)
            .HasInput(SerializationComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Canonical conversion input.", true)
            .HasOutput(SerializationComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Converted value; conversion failures use the message error case.", true)
            .HasEvents(SerializationComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort serialization diagnostics.");
    }

    internal static void ConfigureBase64Decode(ComponentRegistrationBuilder component)
    {
        ConfigureCommon(component, "Base64 Decode", "Decodes a Base64 string value into binary content.", "file-down", "base64Decode");
        component.UseFactory(CreateBase64DecodeNode)
            .HasInput(SerializationComponentDefinition.Ports.Input, static node => node.Input, "Input", "Messages", 0, "Canonical conversion input.", true)
            .HasOutput(SerializationComponentDefinition.Ports.Output, static node => node.Output, "Output", "Results", 1, "Converted value; conversion failures use the message error case.", true)
            .HasEvents(SerializationComponentDefinition.Ports.Events, static node => node.Events, "Events", "Diagnostics", 2, "Best-effort serialization diagnostics.");
    }

    private static void ConfigureCommon(ComponentRegistrationBuilder component, string displayName, string summary, string iconKey, string preferredNodeName)
    {
        var defaults = new SerializationNodeOptions();
        component.WithDisplay(displayName, "Serialization", summary, iconKey, preferredNodeName, 420);
        component.AddOption<int>(SerializationComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<string>(SerializationComponentDefinition.Options.DefaultEncoding, OptionValueKind.Text, "Default Encoding", "Encoding used when content does not declare one.", defaultValue: defaults.DefaultEncoding, section: "Encoding", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Text);
        component.AddOption<int>(SerializationComponentDefinition.Options.MaxInputBytes, OptionValueKind.Number, "Max Input Bytes", "Maximum input payload size accepted by the node.", defaultValue: defaults.MaxInputBytes, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<int>(SerializationComponentDefinition.Options.MaxOutputBytes, OptionValueKind.Number, "Max Output Bytes", "Maximum output payload size emitted by the node.", defaultValue: defaults.MaxOutputBytes, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        component.AddOption<bool>(SerializationComponentDefinition.Options.WriteIndented, OptionValueKind.Boolean, "Write Indented", "Write formatted JSON where the node emits JSON text.", defaultValue: defaults.WriteIndented, section: "JSON", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<bool>(SerializationComponentDefinition.Options.AllowTrailingCommas, OptionValueKind.Boolean, "Allow Trailing Commas", "Allow trailing commas while parsing JSON.", defaultValue: defaults.AllowTrailingCommas, section: "JSON", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddOption<bool>(SerializationComponentDefinition.Options.SkipComments, OptionValueKind.Boolean, "Skip Comments", "Skip comments while parsing JSON.", defaultValue: defaults.SkipComments, section: "JSON", importance: OptionDesignMetadataAttributeValues.Advanced);
        component.AddResource<TimeProvider>(SerializationComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic serialization diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
    }

    private static JsonParseNode CreateJsonParseNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        return new JsonParseNode(options, GetClock(context));
    }

    private static JsonStringifyNode CreateJsonStringifyNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        return new JsonStringifyNode(options, GetClock(context));
    }

    private static TextEncodeNode CreateTextEncodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        return new TextEncodeNode(options, GetClock(context));
    }

    private static TextDecodeNode CreateTextDecodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        return new TextDecodeNode(options, GetClock(context));
    }

    private static Base64EncodeNode CreateBase64EncodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        return new Base64EncodeNode(options, GetClock(context));
    }

    private static Base64DecodeNode CreateBase64DecodeNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<SerializationNodeOptions>();
        return new Base64DecodeNode(options, GetClock(context));
    }

    private static TimeProvider? GetClock(ComponentActivationContext context)
        => context.GetResource<TimeProvider>(SerializationComponentDefinition.Resources.Clock);

}
