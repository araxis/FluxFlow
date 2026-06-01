namespace FluxFlow.Components.Serialization;

public static class SerializationErrorCodes
{
    public const int JsonParseFailed = 10000;
    public const int JsonStringifyFailed = 10010;
    public const int TextEncodeFailed = 10020;
    public const int TextDecodeFailed = 10030;
    public const int Base64EncodeFailed = 10040;
    public const int Base64DecodeFailed = 10050;
    public const int MissingInput = 10100;
    public const int UnsupportedEncoding = 10101;
    public const int InputTooLarge = 10102;
    public const int OutputTooLarge = 10103;
}
