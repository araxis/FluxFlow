using FluxFlow.Components.Serialization.Contracts;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Serialization.Nodes;

internal static class SerializationNodeFactory
{
    public static RuntimeNode CreateJsonParse(RuntimeNodeFactoryContext context)
        => Create<JsonParseRequest, JsonParseResult>(
            context,
            SerializationComponentTypes.JsonParse.Value,
            SerializationConverters.ParseJson,
            SerializationErrorCodes.JsonParseFailed,
            SerializationDiagnosticNames.JsonParsed,
            SerializationDiagnosticNames.JsonParseFailed,
            SerializationConverters.JsonParseInputAttributes,
            SerializationConverters.JsonParseOutputAttributes);

    public static RuntimeNode CreateJsonStringify(RuntimeNodeFactoryContext context)
        => Create<JsonStringifyRequest, JsonStringifyResult>(
            context,
            SerializationComponentTypes.JsonStringify.Value,
            SerializationConverters.StringifyJson,
            SerializationErrorCodes.JsonStringifyFailed,
            SerializationDiagnosticNames.JsonStringified,
            SerializationDiagnosticNames.JsonStringifyFailed,
            SerializationConverters.JsonStringifyInputAttributes,
            SerializationConverters.JsonStringifyOutputAttributes);

    public static RuntimeNode CreateTextEncode(RuntimeNodeFactoryContext context)
        => Create<TextEncodeRequest, TextEncodeResult>(
            context,
            SerializationComponentTypes.TextEncode.Value,
            SerializationConverters.EncodeText,
            SerializationErrorCodes.TextEncodeFailed,
            SerializationDiagnosticNames.TextEncoded,
            SerializationDiagnosticNames.TextEncodeFailed,
            SerializationConverters.TextEncodeInputAttributes,
            SerializationConverters.TextEncodeOutputAttributes);

    public static RuntimeNode CreateTextDecode(RuntimeNodeFactoryContext context)
        => Create<TextDecodeRequest, TextDecodeResult>(
            context,
            SerializationComponentTypes.TextDecode.Value,
            SerializationConverters.DecodeText,
            SerializationErrorCodes.TextDecodeFailed,
            SerializationDiagnosticNames.TextDecoded,
            SerializationDiagnosticNames.TextDecodeFailed,
            SerializationConverters.TextDecodeInputAttributes,
            SerializationConverters.TextDecodeOutputAttributes);

    public static RuntimeNode CreateBase64Encode(RuntimeNodeFactoryContext context)
        => Create<Base64EncodeRequest, Base64EncodeResult>(
            context,
            SerializationComponentTypes.Base64Encode.Value,
            SerializationConverters.EncodeBase64,
            SerializationErrorCodes.Base64EncodeFailed,
            SerializationDiagnosticNames.Base64Encoded,
            SerializationDiagnosticNames.Base64EncodeFailed,
            SerializationConverters.Base64EncodeInputAttributes,
            SerializationConverters.Base64EncodeOutputAttributes);

    public static RuntimeNode CreateBase64Decode(RuntimeNodeFactoryContext context)
        => Create<Base64DecodeRequest, Base64DecodeResult>(
            context,
            SerializationComponentTypes.Base64Decode.Value,
            SerializationConverters.DecodeBase64,
            SerializationErrorCodes.Base64DecodeFailed,
            SerializationDiagnosticNames.Base64Decoded,
            SerializationDiagnosticNames.Base64DecodeFailed,
            SerializationConverters.Base64DecodeInputAttributes,
            SerializationConverters.Base64DecodeOutputAttributes);

    private static RuntimeNode Create<TInput, TOutput>(
        RuntimeNodeFactoryContext context,
        string nodeType,
        Func<TInput, SerializationNodeOptions, TOutput> convert,
        int failureCode,
        string successDiagnosticName,
        string failureDiagnosticName,
        Func<TInput, IReadOnlyDictionary<string, object?>> inputAttributes,
        Func<TOutput, IReadOnlyDictionary<string, object?>> outputAttributes)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = SerializationOptionsReader.ReadNodeOptions(
            context.Definition,
            nodeType);
        var node = new SerializationTransformNode<TInput, TOutput>(
            nodeType,
            options,
            convert,
            failureCode,
            successDiagnosticName,
            failureDiagnosticName,
            inputAttributes,
            outputAttributes);

        return context.CreateNode(node)
            .Input(SerializationComponentPorts.Input, node.Input)
            .Output(SerializationComponentPorts.Output, node.Output)
            .Output(SerializationComponentPorts.Errors, node.Errors)
            .Build();
    }
}
