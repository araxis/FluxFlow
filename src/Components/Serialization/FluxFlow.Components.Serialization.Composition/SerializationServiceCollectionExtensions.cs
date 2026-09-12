using System.Text.Json;
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
            .AddComponent(SerializationComponents.JsonParse)
            .AddComponent(SerializationComponents.JsonStringify)
            .AddComponent(SerializationComponents.TextEncode)
            .AddComponent(SerializationComponents.TextDecode)
            .AddComponent(SerializationComponents.Base64Encode)
            .AddComponent(SerializationComponents.Base64Decode);
    }

    internal static void ConfigureJsonParse(RuntimeComponentRegistrationBuilder component)
    {
        ConfigureCommon(component);
        component.UseFactory(CreateJsonParseNode)
            .HasStandardPorts(FlowValueMaterializers.Content.InputShape);
    }

    internal static void ConfigureJsonStringify(RuntimeComponentRegistrationBuilder component)
    {
        ConfigureCommon(component);
        component.UseFactory(CreateJsonStringifyNode)
            .HasStandardPorts(FlowValueMaterializers.JsonElement.InputShape);
    }

    internal static void ConfigureTextEncode(RuntimeComponentRegistrationBuilder component)
    {
        ConfigureCommon(component);
        component.UseFactory(CreateTextEncodeNode)
            .HasStandardPorts(FlowValueMaterializers.String.InputShape);
    }

    internal static void ConfigureTextDecode(RuntimeComponentRegistrationBuilder component)
    {
        ConfigureCommon(component);
        component.UseFactory(CreateTextDecodeNode)
            .HasStandardPorts(FlowValueMaterializers.Content.InputShape);
    }

    internal static void ConfigureBase64Encode(RuntimeComponentRegistrationBuilder component)
    {
        ConfigureCommon(component);
        component.UseFactory(CreateBase64EncodeNode)
            .HasStandardPorts(FlowValueMaterializers.Content.InputShape);
    }

    internal static void ConfigureBase64Decode(RuntimeComponentRegistrationBuilder component)
    {
        ConfigureCommon(component);
        component.UseFactory(CreateBase64DecodeNode)
            .HasStandardPorts(FlowValueMaterializers.String.InputShape);
    }

    private static void ConfigureCommon(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<int>(SerializationComponentDefinition.Options.BoundedCapacity);
        component.AddOption<string>(SerializationComponentDefinition.Options.DefaultEncoding);
        component.AddOption<int>(SerializationComponentDefinition.Options.MaxInputBytes);
        component.AddOption<int>(SerializationComponentDefinition.Options.MaxOutputBytes);
        component.AddOption<bool>(SerializationComponentDefinition.Options.WriteIndented);
        component.AddOption<bool>(SerializationComponentDefinition.Options.AllowTrailingCommas);
        component.AddOption<bool>(SerializationComponentDefinition.Options.SkipComments);
        component.AddResource<TimeProvider>(SerializationComponentDefinition.Resources.Clock);
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
