using System.Text;
using FluxFlow.Configuration.Expressions;
using Jsonata.Net.Native;

namespace FluxFlow.Studio.UI.Features.Designer.Components.Expressions;

internal static class JsonataExpressionFormatter
{
    public static string Format(string source, bool isCondition = false)
    {
        if (!isCondition && ExpressionSource.IsFileReference(source)) return source;
        var trimmed = source.AsSpan().TrimStart();
        var prefix = trimmed.StartsWith("inline:", StringComparison.OrdinalIgnoreCase)
            ? source[..(source.Length - trimmed.Length + "inline:".Length)] : "";
        var expression = source[prefix.Length..];
        var original = new JsonataQuery(expression).GetAst();
        var tokens = Tokens(expression).ToArray();
        var output = new StringBuilder();
        var groups = new Stack<char>();
        var indent = 0;
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var next = i + 1 < tokens.Length ? tokens[i + 1].Text : "";
            if (token.Text is "}" or "]")
            {
                indent--;
                if (i > 0 && tokens[i - 1].Text is not ("{" or "[")) NewLine();
            }
            if (output.Length == 0 || output[^1] == '\n') output.Append(' ', indent * 2);
            else if (token.Separated && output[^1] != ' ' && token.Text is not ("," or ";" or ":" or ")" or "]" or "}")) output.Append(' ');
            output.Append(token.Text);
            switch (token.Text)
            {
                case "{": case "[":
                    groups.Push(token.Text[0]);
                    indent++;
                    if (next is not ("}" or "]")) NewLine();
                    break;
                case "(": groups.Push('('); break;
                case "}": case "]": case ")": groups.Pop(); break;
                case ",":
                    if (groups.TryPeek(out var group) && group is '{' or '[') NewLine();
                    else output.Append(' ');
                    break;
                case ";": NewLine(); break;
                case ":": output.Append(' '); break;
            }
        }
        var formatted = output.ToString().Trim();
        if (!original.Equals(new JsonataQuery(formatted).GetAst()))
            throw new InvalidOperationException("The expression could not be formatted safely. Its text has been kept unchanged.");
        return prefix + formatted;

        void NewLine()
        {
            while (output.Length > 0 && output[^1] is ' ' or '\t') output.Length--;
            if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
        }
    }

    // Preserve literal and comment text byte-for-byte; only layout between tokens changes.
    private static IEnumerable<(string Text, bool Separated)> Tokens(string text)
    {
        var operand = true;
        var position = 0;
        while (position < text.Length)
        {
            var separated = false;
            while (position < text.Length && char.IsWhiteSpace(text[position])) { separated = true; position++; }
            if (position == text.Length) yield break;
            var start = position;
            var ch = text[position++];
            if (ch == '/' && position < text.Length && text[position] == '*')
            {
                var end = text.IndexOf("*/", position + 1, StringComparison.Ordinal);
                if (end < 0) throw new FormatException("Unclosed JSONata comment.");
                position = end + 2;
                yield return (text[start..position], separated);
                continue;
            }
            if (ch is '"' or '\'' or '`')
            {
                while (position < text.Length)
                {
                    var current = text[position++];
                    if (current == '\\' && ch != '`' && position < text.Length) position++;
                    else if (current == ch) break;
                }
                operand = false;
            }
            else if (ch == '/' && operand)
            {
                var characterClass = false;
                while (position < text.Length)
                {
                    var current = text[position++];
                    if (current == '\\' && position < text.Length) position++;
                    else if (current == '[') characterClass = true;
                    else if (current == ']') characterClass = false;
                    else if (current == '/' && !characterClass) break;
                }
                while (position < text.Length && char.IsLetter(text[position])) position++;
                operand = false;
            }
            else if (char.IsLetterOrDigit(ch) || ch is '$' or '_')
            {
                while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] is '_' or '$')) position++;
                operand = text[start..position] is "and" or "or" or "in";
            }
            else
            {
                if (position < text.Length && text[start..(position + 1)] is ":=" or "~>" or "!=" or "<=" or ">=" or ".." or "**") position++;
                operand = ch is not (')' or ']' or '}');
            }
            yield return (text[start..position], separated);
        }
    }
}
