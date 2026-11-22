using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace FluxFlow.Configuration.Sources;

/// <summary>JSON supplied as text or a file path, with optional inline: and file: prefixes.</summary>
[TypeConverter(typeof(JsonSource.Converter))]
public sealed record JsonSource
{
    public JsonSource(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }
    public static implicit operator JsonSource(string value) => new(value);

    /// <summary>Reads a standalone JSON value without inferring its scalar types.</summary>
    public JsonElement Read(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var text = Value.Trim();
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return ReadFile(text[5..], baseDirectory);
        if (text.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
            return ReadJson(text[7..]);

        try { return ReadJson(text); }
        // An object, array or quoted value is intended as JSON even if malformed.
        // Other non-JSON text is a path; valid JSON scalars always retain their types.
        catch (JsonException) when (text[0] is not ('{' or '[' or '"'))
        {
            return ReadFile(text, baseDirectory);
        }
    }

    public static bool IsFileReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith("inline:", StringComparison.OrdinalIgnoreCase) || text[0] is '[' or '{' or '"') return false;
        try { using var json = JsonDocument.Parse(text); return false; }
        catch (JsonException) { return true; }
    }

    private static JsonElement ReadJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static JsonElement ReadFile(string path, string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = OpenSource(path, baseDirectory);
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static FileStream OpenSource(string path, string baseDirectory)
    {
        try { return File.OpenRead(Path.GetFullPath(path, baseDirectory)); }
        catch (IOException error) when ((error is FileNotFoundException or DirectoryNotFoundException) &&
            Path.DirectorySeparatorChar == '/' && path.Contains('\\') && !Path.IsPathRooted(path) &&
            !path.StartsWith('\\') && !(path.Length >= 2 && path[1] == ':'))
        {
            // Published ZIPs use '/' for directory boundaries. Prefer an existing literal Unix filename.
            var candidate = Path.GetFullPath(path.Replace('\\', '/'), baseDirectory);
            var relative = Path.GetRelativePath(baseDirectory, candidate);
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || !File.Exists(candidate))
                throw;
            return File.OpenRead(candidate);
        }
    }

    /// <summary>Enables scalar binding through the standard .NET configuration binder.</summary>
    public sealed class Converter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            => value is string text ? new JsonSource(text) : base.ConvertFrom(context, culture, value);
    }
}
