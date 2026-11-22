using System.Text;
using System.Text.RegularExpressions;

namespace FluxFlow.Environments;

/// <summary>Edits line-oriented settings without reformatting the surrounding source.</summary>
public sealed class ServiceKeyValueDocument
{
    private const string Word = @"(?:[A-Za-z0-9_-]|\\\.)+";
    private const string Key = Word + @"(?:\." + Word + ")*";
    // Keep quotes in the editable token. A hash outside the token belongs to the source line.
    private const string Value = @"(?:'[^'\r\n\0]*'|[^' \t#\r\n\0][^#\r\n\0]*?)";
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private const string Directive = @"[A-Za-z_][A-Za-z0-9_-]*";
    private const string Arguments = @"[^ \t\r\n\0][^\r\n\0]*?";
    private static readonly Regex Setting = new(@"\A[ \t]*(?<key>" + Key + @")[ \t]*=[ \t]*(?<value>" + Value + @")[ \t]*(?:#.*)?\z", Options);
    private static readonly Regex DirectiveLine = new(@"\A *(?<key>" + Directive + @") +(?<value>" + Arguments + @")[ \t]*\z", Options);
    private static readonly Regex ValidKey = new(@"\A" + Key + @"\z", Options);
    private static readonly Regex ValidValue = new(@"\A" + Value + @"\z", Options);
    private static readonly Regex ValidDirective = new(@"\A" + Directive + @"\z", Options);
    private static readonly Regex ValidArguments = new(@"\A" + Arguments + @"\z", Options);
    private readonly bool _directives;
    private readonly List<Line> _lines = [];
    private readonly string _prefix;
    private readonly string _newline;
    public List<DocumentPair> Entries { get; } = [];

    public ServiceKeyValueDocument(string text) : this(text, ServiceDocumentKind.KeyValueLines) { }

    public ServiceKeyValueDocument(string text, ServiceDocumentKind kind)
    {
        _directives = kind switch
        {
            ServiceDocumentKind.DirectiveLines => true,
            ServiceDocumentKind.KeyValueLines => false,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        _prefix = text.StartsWith('\uFEFF') ? "\uFEFF" : "";
        _newline = Regex.Match(text, @"\r\n|\r|\n") is { Success: true } ending ? ending.Value : "\n";
        foreach (Match match in Regex.Matches(text[_prefix.Length..], @"[^\r\n]+(?:\r\n|\r|\n)?|\r\n|\r|\n"))
        {
            var original = match.Value;
            var content = original.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(content) || (_directives ? content : content.TrimStart()).StartsWith('#'))
            {
                _lines.Add(new(original));
                continue;
            }

            var setting = (_directives ? DirectiveLine : Setting).Match(content);
            if (!setting.Success) throw new ArgumentException("This configuration contains syntax that requires Advanced text.");
            var key = setting.Groups["key"];
            var value = setting.Groups["value"];
            var argument = value.Value;
            var entry = new DocumentPair { Name = key.Value, Value = argument, OriginalValue = argument };
            Entries.Add(entry);
            _lines.Add(new(original, entry, key.Index, key.Length, value.Index, value.Length));
        }
        Validate();
    }

    public string Serialize()
    {
        Validate();
        var remaining = Entries.ToHashSet();
        var result = new StringBuilder(_prefix);
        foreach (var line in _lines)
        {
            if (line.Entry is null) result.Append(line.Text);
            else if (remaining.Remove(line.Entry))
            {
                result.Append(line.Text.AsSpan(0, line.NameStart)).Append(line.Entry.Name)
                    .Append(line.Text.AsSpan(line.NameEnd, line.ValueStart - line.NameEnd)).Append(line.Entry.Value)
                    .Append(line.Text.AsSpan(line.ValueEnd));
            }
        }
        foreach (var entry in Entries.Where(remaining.Contains))
        {
            if (result.Length > _prefix.Length && result[^1] is not ('\n' or '\r')) result.Append(_newline);
            result.Append(entry.Name).Append(_directives ? " " : " = ").Append(entry.Value).Append(_newline);
        }
        return result.ToString();
    }

    private void Validate()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            if (_directives)
            {
                if (!ValidDirective.IsMatch(entry.Name ?? "")) throw new ArgumentException("Enter a directive name.");
                if (!ValidArguments.IsMatch(entry.Value ?? "")) throw new ArgumentException("Enter a non-empty, single-line argument.");
                continue;
            }
            if (!ValidKey.IsMatch(entry.Name)) throw new ArgumentException("Enter a key with non-empty dot-separated segments.");
            if (!ValidValue.IsMatch(entry.Value)) throw new ArgumentException("Enter a single-line value. Use single quotes for an empty value or a literal #.");
            if (!names.Add(entry.Name)) throw new ArgumentException($"Duplicate name: {entry.Name}");
        }
    }

    private sealed record Line(string Text, DocumentPair? Entry = null, int NameStart = 0, int NameLength = 0,
        int ValueStart = 0, int ValueLength = 0)
    {
        public int NameEnd => NameStart + NameLength;
        public int ValueEnd => ValueStart + ValueLength;
    }
}
