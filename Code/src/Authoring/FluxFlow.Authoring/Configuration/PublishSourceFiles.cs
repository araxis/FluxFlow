using System.Text.Json;
using System.Text.RegularExpressions;
using FluxFlow.Configuration.Sources;
using FluxFlow.Configuration.Expressions;
using FluxFlow.Plugins;

namespace FluxFlow.Authoring;

public static class PublishSourceFiles
{
    // Use the installed component's editor definition, including older plugin packages.
    public static string[] Find(DesignerConfiguration configuration, IEnumerable<IComponentDescription> components) =>
        Find(configuration, components, forRunner: true);

    public static string[] Find(DesignerConfiguration configuration, IEnumerable<IComponentDescription> components, bool forRunner)
    {
        var fields = components.ToDictionary(c => c.Palette.ComponentType, c => (
            NamedExpressions: forRunner ? null : (c as PluginComponent)?.NamedOutputs?.Property,
            Options: c.Inspector.Sections.SelectMany(s => s.Options).Where(o => o.Editor == OptionEditorKind.JsonSource ||
                (!forRunner && o.Editor == OptionEditorKind.Expression))
                .ToLookup(o => o.Name, o => o.Editor, StringComparer.OrdinalIgnoreCase)), StringComparer.Ordinal);
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var workflow in configuration.WorkflowNames)
        foreach (var node in configuration.Nodes(workflow))
        {
            if (!fields.TryGetValue(node.Type, out var names)) continue;
            foreach (var (key, value) in node.Values)
            {
                if (value.ValueKind == JsonValueKind.Object && key.Equals(names.NamedExpressions, StringComparison.OrdinalIgnoreCase))
                    foreach (var route in value.EnumerateObject())
                        if (route.Value.ValueKind == JsonValueKind.String && ExpressionSource.IsFileReference(route.Value.GetString()))
                            AddPath(route.Value.GetString()!.Trim());
                if (value.ValueKind != JsonValueKind.String || !names.Options.Contains(key)) continue;
                var text = value.GetString()!.Trim();
                if (!names.Options[key].Any(editor => editor == OptionEditorKind.Expression
                    ? ExpressionSource.IsFileReference(text) : JsonSource.IsFileReference(text))) continue;
                AddPath(text);
            }
        }
        var portable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            if (!portable.Add(path))
                throw new InvalidOperationException($"Conflicting Source file paths at '{path}'.");
        foreach (var path in paths)
            if (Parents(path).Any(portable.Contains))
                throw new InvalidOperationException($"Conflicting Source file paths at '{path}'.");
        return paths.ToArray();

        void AddPath(string text)
        {
            var path = text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? text[5..] : text;
            if (RelativePath(path, forRunner) is { } relative) paths.Add(relative);
        }
    }

    public static IEnumerable<string> Parents(string path)
    {
        for (var i = path.IndexOf('/'); i >= 0; i = path.IndexOf('/', i + 1))
            yield return path[..i];
    }

    private static string? RelativePath(string path, bool forRunner)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || Regex.IsMatch(normalized, "^[a-zA-Z]:/")) return null;
        var segments = new List<string>();
        foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (segments.Count == 0) throw new InvalidOperationException($"Source file '{path}' is outside the Runner directory.");
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            if (part.EndsWith('.') || part.EndsWith(' ') || part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
                Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|(?:COM|LPT)[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidOperationException($"Source file '{path}' is not a portable file path.");
            segments.Add(part);
        }
        if (segments.Count == 0 || normalized[(normalized.LastIndexOf('/') + 1)..] is "" or "." or "..")
            throw new InvalidOperationException($"Source file '{path}' must name a file.");
        // The host reads these optional settings on startup even in a distribution without them.
        if (forRunner && (Regex.IsMatch(segments[0], @"^appsettings(?:\..+)?\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            segments[0].Equals("FluxFlow.Runner.runtimeconfig.dev.json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Source file conflicts with Runner settings '{segments[0]}'.");
        return string.Join('/', segments);
    }
}
