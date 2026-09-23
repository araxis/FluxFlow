using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace FluxFlow.Configuration.Expressions;

/// <summary>An expression supplied as inline text, a file path or explicit UTF-8 Base64 text.</summary>
[TypeConverter(typeof(ExpressionSource.Converter))]
public sealed record ExpressionSource
{
    public ExpressionSource(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static implicit operator ExpressionSource(string value) => new(value);

    /// <summary>Resolves text during setup/build, never during message processing.</summary>
    public string Read(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var text = Value.Trim();
        if (text.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
            return RequireText(text[7..]);
        if (text.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
            return RequireText(new UTF8Encoding(false, true).GetString(Convert.FromBase64String(text[7..])));
        var explicitFile = text.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        if (!explicitFile && !text.EndsWith(".jsonata", StringComparison.OrdinalIgnoreCase)
            && !text.EndsWith(".csx", StringComparison.OrdinalIgnoreCase)
            && !text.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return text;
        var path = explicitFile ? RequireText(text[5..]) : text;
        return RequireText(File.ReadAllText(Path.GetFullPath(path, baseDirectory)));
    }

    private static string RequireText(string text)
        => !string.IsNullOrWhiteSpace(text) ? text
            : throw new ArgumentException("Expression text or file is empty.");

    /// <summary>Allows the standard configuration binder to read a scalar source.</summary>
    public sealed class Converter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            => value is string text ? new ExpressionSource(text) : base.ConvertFrom(context, culture, value);
    }
}
