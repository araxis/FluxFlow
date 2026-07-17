using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace FluxFlow.Data;

public sealed class BinaryFlowContentCodec : IFlowContentCodec
{
    public FlowValue Decode(ImmutableArray<byte> content, string? encoding)
        => FlowValue.FromBinary(content);
}

public sealed class TextFlowContentCodec : IFlowContentCodec
{
    public FlowValue Decode(ImmutableArray<byte> content, string? encoding)
        => FlowValue.From(FlowTextEncoding.Resolve(encoding).GetString(content.AsSpan()));
}

public sealed class JsonFlowContentCodec : IFlowContentCodec
{
    public FlowValue Decode(ImmutableArray<byte> content, string? encoding)
    {
        var json = FlowTextEncoding.Resolve(encoding).GetString(content.AsSpan());
        using var document = JsonDocument.Parse(json);
        return Convert(document.RootElement);
    }

    private static FlowValue Convert(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null => FlowValue.Null,
            JsonValueKind.False => FlowValue.From(false),
            JsonValueKind.True => FlowValue.From(true),
            JsonValueKind.String => FlowValue.From(element.GetString()!),
            JsonValueKind.Number => ConvertNumber(element.GetRawText()),
            JsonValueKind.Array => FlowValue.FromArray(element.EnumerateArray().Select(Convert)),
            JsonValueKind.Object => ConvertObject(element),
            _ => throw new JsonException($"JSON value kind '{element.ValueKind}' is not supported.")
        };

    private static FlowValue ConvertNumber(string value)
    {
        try
        {
            if (!value.Contains('.') && value.IndexOfAny(['e', 'E']) < 0)
                return FlowValue.From(BigInteger.Parse(value, CultureInfo.InvariantCulture));

            if (decimal.TryParse(
                value,
                NumberStyles.Number | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out var decimalValue))
            {
                return FlowValue.From(decimalValue);
            }

            return FlowValue.From(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            throw new JsonException("JSON number is outside the supported FlowValue range.", exception);
        }
    }

    private static FlowValue ConvertObject(JsonElement element)
    {
        try
        {
            return FlowValue.FromObject(element.EnumerateObject().Select(
                property => new KeyValuePair<string, FlowValue>(property.Name, Convert(property.Value))));
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("JSON object contains duplicate properties.", exception);
        }
    }
}

internal static class FlowTextEncoding
{
    public static Encoding Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(name.Trim().Trim('"'));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }
}
