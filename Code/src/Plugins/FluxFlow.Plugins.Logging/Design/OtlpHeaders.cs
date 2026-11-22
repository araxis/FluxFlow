using System.Text.Json;

namespace FluxFlow.Plugins.Logging;

public static class OtlpHeaders
{
    public static void ValidateJson(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException("Enter a comma-separated header string.");
        ValidateList(value.GetString()!);
    }

    public static void ValidateList(string headers)
    {
        // One request validates repeated names according to native HTTP header rules.
        using var request = new HttpRequestMessage();
        foreach (var part in headers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || string.IsNullOrWhiteSpace(part[..separator]) || part.Contains('\r') || part.Contains('\n'))
                throw new ArgumentException("Headers must contain comma-separated name=value entries.");
            try
            {
                request.Headers.Add(Uri.UnescapeDataString(part[..separator].Trim()), Uri.UnescapeDataString(part[(separator + 1)..].Trim()));
            }
            catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
            {
                throw new ArgumentException("Enter valid HTTP request header names and values.");
            }
        }
    }

    public static StringMapEntry[]? Read(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) return [];
        if (value.ValueKind != JsonValueKind.String) return null;
        var entries = new List<StringMapEntry>();
        foreach (var part in value.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || part.Contains('\r') || part.Contains('\n')) return null;
            var entry = new StringMapEntry(Uri.UnescapeDataString(part[..separator].Trim()), Uri.UnescapeDataString(part[(separator + 1)..].Trim()));
            if (Validate(entry.Name, entry.Value) is not null || entries.Any(item => item.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))) return null;
            entries.Add(entry);
        }
        return entries.ToArray();
    }

    public static JsonElement Write(JsonElement original, IReadOnlyList<StringMapEntry> entries)
    {
        var saved = Read(original) ?? throw new InvalidOperationException("Use Advanced JSON to repair these headers first.");
        var parts = original.ValueKind == JsonValueKind.String
            ? original.GetString()!.Split(',').Where(part => !string.IsNullOrWhiteSpace(part)).ToArray() : [];
        return JsonSerializer.SerializeToElement(string.Join(',', entries.Select(entry =>
        {
            var index = Array.IndexOf(saved, entry);
            return index >= 0 ? parts[index] : Uri.EscapeDataString(entry.Name) + "=" + Uri.EscapeDataString(entry.Value);
        })));
    }

    public static string? Validate(string name, string value)
    {
        try
        {
            if (name.Contains('\r') || name.Contains('\n') || value.Contains('\r') || value.Contains('\n'))
                return "Header names and values cannot contain line breaks.";
            using var request = new HttpRequestMessage();
            request.Headers.Add(name, value);
            return null;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
        { return "Enter a valid HTTP request header name and value."; }
    }
}
