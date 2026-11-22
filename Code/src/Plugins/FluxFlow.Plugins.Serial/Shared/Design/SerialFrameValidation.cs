using System.Globalization;
using System.Text.Json;
using FluxFlow.Components.Serial;

namespace FluxFlow.Plugins.Serial;

internal static class SerialFrameValidation
{
    private static readonly SerialFrameOptions Defaults = new();

    // The field editors handle malformed individual values. Compare the sizes independently
    // so that an unrelated invalid setting cannot hide a repairable size mismatch.
    public static IReadOnlyDictionary<string, string> Fields(IDictionary<string, JsonElement> values)
    {
        var errors = new Dictionary<string, string>();
        var maximum = Number(nameof(Defaults.MaxFrameBytes), Defaults.MaxFrameBytes);
        if (maximum is null or < 1 or > SerialFrameOptions.FrameSizeLimit) return errors;
        var readBuffer = Number(nameof(Defaults.ReadBufferBytes), Defaults.ReadBufferBytes);
        if (readBuffer <= SerialFrameOptions.ReadBufferLimit && readBuffer > maximum)
            errors[nameof(Defaults.ReadBufferBytes)] = "Read buffer must not exceed Max frame bytes.";
        var frameLength = Number(nameof(Defaults.FrameLength), Defaults.FrameLength);
        if (Enum.TryParse<SerialFraming>(Text(nameof(Defaults.Framing)) ?? Defaults.Framing.ToString(), true, out var framing)
            && framing == SerialFraming.FixedLength && frameLength <= SerialFrameOptions.FrameSizeLimit && frameLength > maximum)
            errors[nameof(Defaults.FrameLength)] = "Frame bytes must not exceed Max frame bytes.";
        return errors;

        string? Text(string name) => values.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value
            is { ValueKind: not JsonValueKind.Undefined } value ? value.ToString() : null;
        int? Number(string name, int fallback) => Text(name) is not { } text ? fallback
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}
