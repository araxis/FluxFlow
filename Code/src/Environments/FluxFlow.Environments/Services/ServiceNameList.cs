using System.Text.RegularExpressions;

namespace FluxFlow.Environments;

/// <summary>A portable editor for a delimited list of unique names in a startup file.</summary>
public sealed class ServiceNameList(string item, string items, string namePattern, string open, string separator, string close, string terminator)
{
    public string Item { get; } = item;
    public string Items { get; } = items;
    public int MaxNameLength { get; init; } = int.MaxValue;
    private readonly Regex _name = new(@"\A(?:" + namePattern + @")\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public string[] Read(string text)
    {
        var content = text.AsSpan().Trim();
        if (!content.EndsWith(terminator, StringComparison.Ordinal)) throw Unsupported();
        content = content[..(content.Length - terminator.Length)].TrimEnd();
        if (!content.StartsWith(open, StringComparison.Ordinal) || content.Length < open.Length + close.Length ||
            !content.EndsWith(close, StringComparison.Ordinal)) throw Unsupported();
        content = content[open.Length..(content.Length - close.Length)].Trim();
        if (content.IsEmpty) return [];
        var names = content.ToString().Split(separator, StringSplitOptions.TrimEntries);
        Validate(names);
        return names;
    }

    public string Write(string original, IReadOnlyList<string> names)
    {
        Validate(names);
        return Read(original).SequenceEqual(names) ? original :
            open + string.Join(separator, names) + close + terminator + (original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n");
    }

    private void Validate(IReadOnlyList<string> names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (name.Length > MaxNameLength) throw new ArgumentException($"{Item} names cannot exceed {MaxNameLength} characters.");
            if (!_name.IsMatch(name)) throw new ArgumentException($"Invalid {Item.ToLowerInvariant()} name: {name}");
            if (!seen.Add(name)) throw new ArgumentException($"Duplicate name: {name}");
        }
    }

    private static ArgumentException Unsupported() => new("This list uses syntax that requires Advanced text.");
}
