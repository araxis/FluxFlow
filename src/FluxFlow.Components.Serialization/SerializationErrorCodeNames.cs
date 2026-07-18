namespace FluxFlow.Components.Serialization;

public static class SerializationErrorCodeNames
{
    public const string JsonParseFailed = "serialization.json_parse_failed";
    public const string JsonStringifyFailed = "serialization.json_stringify_failed";
    public const string TextEncodeFailed = "serialization.text_encode_failed";
    public const string TextDecodeFailed = "serialization.text_decode_failed";
    public const string Base64EncodeFailed = "serialization.base64_encode_failed";
    public const string Base64DecodeFailed = "serialization.base64_decode_failed";
    public const string MissingInput = "serialization.missing_input";
    public const string InputTooLarge = "serialization.input_too_large";
    public const string OutputTooLarge = "serialization.output_too_large";
}
