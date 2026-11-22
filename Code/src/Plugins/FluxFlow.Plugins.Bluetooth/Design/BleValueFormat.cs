using System.Text;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Authoring;

namespace FluxFlow.Plugins.Bluetooth.Design;

public static class BleValueFormat
{
    public static EncodedValue ToEditor(BlePayload value) => new(value.Encoding.ToString(), value.Data);
    public static BlePayload FromEditor(EncodedValue value) => new() { Encoding = Enum.Parse<BleValueEncoding>(value.Encoding), Data = value.Data };
    private static byte[] Bytes(EncodedValue value)
    {
        try { return FromEditor(value).Decode(); }
        catch (FormatException error)
        {
            throw new FormatException(value.Encoding == nameof(BleValueEncoding.Hex)
                ? "Enter valid hexadecimal pairs (for example, 00FF)." : "Enter a valid Base64 value.", error);
        }
    }
    private static EncodedValue ConvertValue(EncodedValue value, string encoding)
    {
        var bytes = Bytes(value);
        try
        {
            return new(encoding, encoding switch
            {
                nameof(BleValueEncoding.Hex) => Convert.ToHexString(bytes),
                nameof(BleValueEncoding.Base64) => Convert.ToBase64String(bytes),
                nameof(BleValueEncoding.Utf8) => new UTF8Encoding(false, true).GetString(bytes),
                _ => throw new ArgumentException("Unknown encoding.")
            });
        }
        catch (DecoderFallbackException error)
        { throw new ArgumentException("These bytes cannot be represented as UTF-8. Choose Hex or Base64.", error); }
    }
    private static readonly BinaryValueFormat Raw = new(
        [OptionChoiceModel.FromEnum(BleValueEncoding.Hex), OptionChoiceModel.FromEnum(BleValueEncoding.Base64), OptionChoiceModel.FromEnum(BleValueEncoding.Utf8, "UTF-8")],
        512, value => Bytes(value).Length, ConvertValue);

    public static BinaryValueFormat For(string? characteristic = null) =>
        Guid.TryParse(characteristic, out var id) && BleStandards.HasCodec(id)
            ? Raw with { Codec = new(value => BleStandards.Decode(id, Bytes(value)), text => ToEditor(BleStandards.Encode(id, text))) }
            : Raw;
}
