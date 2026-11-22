using System.Text.Json;

namespace FluxFlow.Environments;

/// <summary>An isolated edit of the string map used for environment variables or startup files.</summary>
public sealed class ServiceStringMapDocument
{
    private readonly ServiceDocumentKind _kind;
    private readonly string _source;
    private readonly Dictionary<string, string> _original;
    public List<DocumentPair> Entries { get; } = [];

    public ServiceStringMapDocument(ServiceDocumentKind kind, string source)
    {
        _kind = kind;
        _source = source;
        _original = ParseSource(kind, source);
        foreach (var (name, value) in _original)
            Entries.Add(new() { Name = name, Value = value });
    }

    public static void ValidateSource(ServiceDocumentKind kind, string source) => _ = ParseSource(kind, source);

    private static Dictionary<string, string> ParseSource(ServiceDocumentKind kind, string source)
    {
        if (kind is not (ServiceDocumentKind.Variables or ServiceDocumentKind.Files))
            throw new ArgumentOutOfRangeException(nameof(kind));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var json = JsonDocument.Parse(source.StartsWith('\uFEFF') ? source.AsMemory(1) : source.AsMemory());
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Settings must be a JSON object.");
        foreach (var property in json.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) throw new ArgumentException("Names and values must be strings.");
            var value = property.Value.GetString()!;
            AddValue(values, kind, property.Name, value);
        }
        return values;
    }

    public static string Read(ResourceBinding binding, ServiceDocumentKind kind) =>
        JsonSerializer.Serialize(Map(binding, kind), EnvironmentJsonContext.Default.DictionaryStringString);

    public ResourceBinding Apply(ResourceBinding binding) => Apply(binding, []);

    public ResourceBinding Apply(ResourceBinding binding, IReadOnlyList<ServiceStartupFile> startupFiles)
    {
        if (binding.Profile?.SetupLocked == true) return binding;
        var values = ValidatedMap();
        if (SameValues(Map(binding, _kind), values)) return binding;
        if (_kind == ServiceDocumentKind.Files)
        {
            foreach (var file in startupFiles)
                if (file.IsGenerated(binding) && values.TryGetValue(file.Path, out var text) && text != binding.Files.GetValueOrDefault(file.Path))
                    throw new ArgumentException($"Cannot override '{file.Path}' while it is generated from configured users.");
        }
        return _kind == ServiceDocumentKind.Files ? binding with { Files = values } : binding with { Variables = values };
    }

    public string Serialize()
    {
        var values = ValidatedMap();
        return SameValues(_original, values) ? _source :
            (_source.StartsWith('\uFEFF') ? "\uFEFF" : "") + JsonSerializer.Serialize(values, EnvironmentJsonContext.Default.DictionaryStringString);
    }

    private Dictionary<string, string> ValidatedMap()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Entries)
            AddValue(values, _kind, entry.Name, entry.Value ?? "");
        return values;
    }

    private static void AddValue(Dictionary<string, string> values, ServiceDocumentKind kind, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Enter a name for every entry.");
        if (name.IndexOfAny(['\r', '\n', '\0']) >= 0) throw new ArgumentException("Names cannot contain line breaks or null characters.");
        if (kind == ServiceDocumentKind.Variables && name.Contains('=')) throw new ArgumentException("Variable names cannot contain '='.");
        if (kind == ServiceDocumentKind.Files && !name.StartsWith('/')) throw new ArgumentException("Use an absolute container path starting with '/'.");
        if (!values.TryAdd(name, value)) throw new ArgumentException($"Duplicate name: {name}");
    }

    private static Dictionary<string, string> Map(ResourceBinding binding, ServiceDocumentKind kind) => kind switch
    {
        ServiceDocumentKind.Variables => binding.Variables,
        ServiceDocumentKind.Files => binding.Files,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool SameValues(Dictionary<string, string> original, Dictionary<string, string> values) =>
        original.Count == values.Count && original.All(pair => values.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
