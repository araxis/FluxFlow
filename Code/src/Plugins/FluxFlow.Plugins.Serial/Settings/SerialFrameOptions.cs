using System.Text;

namespace FluxFlow.Components.Serial;

public enum SerialFraming { Raw, Delimiter, FixedLength }
public enum SerialFrameOutputFormat { Base64, Bytes }
public enum SerialTextEncoding { None, Utf8, Ascii, Utf16LittleEndian, Utf16BigEndian }

public sealed record SerialFrameOptions
{
    public const int FrameSizeLimit = 1048576;
    public const int ReadBufferLimit = 65536;

    public SerialFraming Framing { get; init; } = SerialFraming.Delimiter;
    public SerialFrameOutputFormat OutputFormat { get; init; } = SerialFrameOutputFormat.Base64;
    public string DelimiterBase64 { get; init; } = "Cg==";
    public int FrameLength { get; init; } = 1;
    public int MaxFrameBytes { get; init; } = 65536;
    public int ReadBufferBytes { get; init; } = 1024;
    public SerialTextEncoding Encoding { get; init; } = SerialTextEncoding.Utf8;
    public int Capacity { get; init; } = 128;

    public void Validate()
    {
        if (!Enum.IsDefined(Framing)) throw new ArgumentOutOfRangeException(nameof(Framing));
        if (!Enum.IsDefined(OutputFormat)) throw new ArgumentOutOfRangeException(nameof(OutputFormat));
        if (!Enum.IsDefined(Encoding)) throw new ArgumentOutOfRangeException(nameof(Encoding));
        if (MaxFrameBytes is < 1 or > FrameSizeLimit) throw new ArgumentOutOfRangeException(nameof(MaxFrameBytes));
        if (ReadBufferBytes is < 1 or > ReadBufferLimit || ReadBufferBytes > MaxFrameBytes) throw new ArgumentOutOfRangeException(nameof(ReadBufferBytes));
        if (Capacity is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(Capacity));
        if (Framing == SerialFraming.FixedLength && (FrameLength < 1 || FrameLength > MaxFrameBytes))
            throw new ArgumentOutOfRangeException(nameof(FrameLength));
        if (Framing == SerialFraming.Delimiter && Convert.FromBase64String(DelimiterBase64).Length is < 1 or > 64)
            throw new ArgumentException("DelimiterBase64 must contain between 1 and 64 bytes.");
    }

    public Encoding? GetEncoding() => Encoding switch
    {
        SerialTextEncoding.None => null,
        SerialTextEncoding.Utf8 => new UTF8Encoding(false, true),
        SerialTextEncoding.Ascii => System.Text.Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        SerialTextEncoding.Utf16LittleEndian => new UnicodeEncoding(false, false, true),
        SerialTextEncoding.Utf16BigEndian => new UnicodeEncoding(true, false, true),
        _ => throw new ArgumentOutOfRangeException(nameof(Encoding))
    };
}
