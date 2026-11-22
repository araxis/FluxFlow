using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

public sealed record EncodedValue(string Encoding, string Data);
public sealed record ValueCodec(Func<EncodedValue, string> Describe, Func<string, EncodedValue> Encode);

/// <summary>Converts representations of the same bytes. Codec and size limits belong to the feature.</summary>
public sealed record BinaryValueFormat(IReadOnlyList<OptionChoiceModel> Encodings, int MaximumBytes,
    Func<EncodedValue, int> ByteLength, Func<EncodedValue, string, EncodedValue> Convert, ValueCodec? Codec = null);
