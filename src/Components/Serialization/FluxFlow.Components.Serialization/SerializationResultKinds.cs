namespace FluxFlow.Components.Serialization;

public static class SerializationResultKinds
{
    public const string JsonParsed = "JsonParsed";
    public const string JsonParseFailed = "JsonParseFailed";
    public const string JsonStringified = "JsonStringified";
    public const string JsonStringifyFailed = "JsonStringifyFailed";
    public const string TextEncoded = "TextEncoded";
    public const string TextEncodeFailed = "TextEncodeFailed";
    public const string TextDecoded = "TextDecoded";
    public const string TextDecodeFailed = "TextDecodeFailed";
    public const string Base64Encoded = "Base64Encoded";
    public const string Base64EncodeFailed = "Base64EncodeFailed";
    public const string Base64Decoded = "Base64Decoded";
    public const string Base64DecodeFailed = "Base64DecodeFailed";
}
