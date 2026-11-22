using FluxFlow.Components.Serial;

namespace FluxFlow.Configuration.Components.Serial;

public sealed record SerialFrameConfiguration : ComponentConfiguration
{
    public string Connection { get; init; } = "";
    public SerialFraming Framing { get; init; } = SerialFraming.Delimiter;
    public SerialFrameOutputFormat OutputFormat { get; init; } = SerialFrameOutputFormat.Base64;
    public string DelimiterBase64 { get; init; } = "Cg==";
    public int FrameLength { get; init; } = 1;
    public int MaxFrameBytes { get; init; } = 65536;
    public int ReadBufferBytes { get; init; } = 1024;
    public SerialTextEncoding Encoding { get; init; } = SerialTextEncoding.Utf8;
    public int Capacity { get; init; } = 128;
    public SerialFrameOptions Options() => new()
    {
        Framing = Framing, OutputFormat = OutputFormat, DelimiterBase64 = DelimiterBase64, FrameLength = FrameLength,
        MaxFrameBytes = MaxFrameBytes, ReadBufferBytes = ReadBufferBytes, Encoding = Encoding, Capacity = Capacity
    };
}
