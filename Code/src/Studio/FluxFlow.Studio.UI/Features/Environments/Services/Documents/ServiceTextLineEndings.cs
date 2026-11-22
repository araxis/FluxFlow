using System.Text;
using System.Text.RegularExpressions;

namespace FluxFlow.Studio.UI.Features.Environments.Services.Documents;

internal static class ServiceTextLineEndings
{
    // Browser text controls normalize CR and CRLF but leave other Unicode separators intact.
    public static string Display(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');

    public static string Restore(string previous, string text)
    {
        if (previous == text || !previous.Contains('\r')) return text;
        text = Display(text);
        if (!text.Contains('\n')) return text;

        var old = Regex.Split(previous, @"(\r\n|\r|\n)");
        var lines = text.Split('\n');
        var oldBreaks = old.Length / 2;
        var newBreaks = lines.Length - 1;

        // Keep the unchanged tail anchored when lines are inserted or removed.
        var tail = 0;
        while (tail < Math.Min(oldBreaks, newBreaks) && old[old.Length - 1 - 2 * tail] == lines[lines.Length - 1 - tail])
            tail++;

        // Deleting complete lines must not replace the separator on the last unchanged prefix line.
        if (oldBreaks > newBreaks)
        {
            var prefix = 0;
            while (prefix < newBreaks && old[2 * prefix] == lines[prefix]) prefix++;
            tail = Math.Min(tail, newBreaks - prefix);
        }

        var result = new StringBuilder(text.Length);
        for (var i = 0; i < newBreaks; i++)
        {
            var index = i < newBreaks - tail ? Math.Min(i, oldBreaks - tail) : i + oldBreaks - newBreaks;
            result.Append(lines[i]).Append(old[2 * Math.Min(index, oldBreaks - 1) + 1]);
        }
        return result.Append(lines[^1]).ToString();
    }
}
