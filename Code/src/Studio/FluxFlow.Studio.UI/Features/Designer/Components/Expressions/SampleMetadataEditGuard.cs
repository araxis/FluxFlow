using System.Text;
using System.Text.Json;

namespace FluxFlow.Studio.UI.Features.Designer.Components.Expressions;

// Keeps the JSON shell intact while allowing unfinished values during typing.
public sealed class SampleMetadataEditGuard
{
    private string _text = "";
    private List<(int Start, int End)> _values = [];

    public void Initialize(string text)
    {
        if (!TryReadObject(text, out var values)) throw new JsonException("Expected the sample JSON object.");
        _text = text;
        _values = values;
    }

    public bool TryAccept(string text)
    {
        if (text == _text) return true;
        try
        {
            if (!TryReadObject(text, out var values)) return false;
            _text = text;
            _values = values;
            return true;
        }
        catch (JsonException)
        {
            // Invalid JSON is a normal intermediate state when replacing a value.
            // Only edits entirely inside an existing editable range may reach it.
            var start = 0;
            while (start < text.Length && start < _text.Length && text[start] == _text[start]) start++;
            var oldEnd = _text.Length;
            var newEnd = text.Length;
            while (oldEnd > start && newEnd > start && _text[oldEnd - 1] == text[newEnd - 1]) { oldEnd--; newEnd--; }
            var index = _values.FindIndex(range => start >= range.Start && oldEnd <= range.End);
            if (index < 0) return false;
            var delta = text.Length - _text.Length;
            _values[index] = (_values[index].Start, _values[index].End + delta);
            for (var i = index + 1; i < _values.Count; i++)
                _values[i] = (_values[i].Start + delta, _values[i].End + delta);
            _text = text;
            return true;
        }
    }

    private bool TryReadObject(string text, out List<(int Start, int End)> values)
    {
        values = [];
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
        if (!HasProperties(document.RootElement, SampleMetadataJson.Properties) ||
            document.RootElement.GetProperty("$headers").ValueKind != JsonValueKind.Object) return false;

        var bytes = Encoding.UTF8.GetBytes(text);
        List<(int Start, int End)> ranges = [];
        var reader = new Utf8JsonReader(bytes);
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1) continue;
            var headers = reader.ValueTextEquals("$headers");
            reader.Read();
            if (headers)
            {
                var start = (int)reader.BytesConsumed;
                reader.Skip();
                AddRange(start, (int)reader.TokenStartIndex);
            }
            else
            {
                var start = (int)reader.TokenStartIndex;
                reader.Skip();
                AddRange(start, (int)reader.BytesConsumed);
            }
        }
        values = ranges;
        return true;

        void AddRange(int start, int end) => ranges.Add((Encoding.UTF8.GetCharCount(bytes.AsSpan(0, start)),
            Encoding.UTF8.GetCharCount(bytes.AsSpan(0, end))));
    }

    private static bool HasProperties(JsonElement element, IReadOnlyList<string> expected)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = element.EnumerateObject().Select(property => property.Name).ToArray();
        return names.Length == expected.Count && names.Distinct(StringComparer.Ordinal).Count() == names.Length &&
            names.All(name => expected.Contains(name, StringComparer.Ordinal));
    }
}
