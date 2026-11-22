using FluxFlow.Environments;

namespace FluxFlow.Studio.UI.Features.Environments.Services.Documents;

public sealed class ServiceDocumentDraft
{
    private readonly ServiceDirectiveSchema? _directives;
    private readonly string _original;
    private readonly ServiceNameList? _names;
    private readonly ServiceStringMapDocument? _map;
    private readonly ServiceKeyValueDocument? _settings;
    public ServiceDocumentKind Kind { get; }
    public List<DocumentPair> Entries { get; } = [];
    public IReadOnlyList<string> PreviewItems { get; } = [];
    public ServiceCollectionDocument? Collections { get; }

    public ServiceDocumentDraft(ServiceDocumentKind kind, string text, ServiceCollectionSchema? templates = null, ServiceNameList? names = null, ServiceDirectiveSchema? directives = null, ServiceTextPreview? preview = null)
    {
        _names = names;
        _directives = directives;
        Kind = kind;
        _original = text;
        if (kind is ServiceDocumentKind.KeyValueLines or ServiceDocumentKind.DirectiveLines)
        {
            _settings = new(text, kind);
            Entries = _settings.Entries;
            ValidateDirectives();
        }
        else if (kind == ServiceDocumentKind.TextPreview)
            PreviewItems = (preview ?? throw new ArgumentException("No preview is available.")).Read(text);
        else if (kind == ServiceDocumentKind.NameList)
        {
            if (names is null) throw new ArgumentException("This file requires Advanced text.");
            Entries.AddRange(names.Read(text).Select(name => new DocumentPair { Name = name }));
        }
        else if (kind == ServiceDocumentKind.ObjectCollections)
            Collections = (templates ?? throw new ArgumentException("Collection templates are required.")).Open(text);
        else
        {
            _map = new(kind, text);
            Entries = _map.Entries;
        }
    }

    private void ValidateDirectives()
    {
        if (_directives is not null) Entries.ForEach(_directives.Validate);
    }

    public string Serialize()
    {
        if (Kind == ServiceDocumentKind.TextPreview) return _original;
        if (Collections is not null) return Collections.Serialize();
        if (Kind == ServiceDocumentKind.NameList) return _names!.Write(_original, Entries.Select(entry => entry.Name).ToArray());
        if (_settings is not null)
        {
            ValidateDirectives();
            return _settings.Serialize();
        }
        return _map!.Serialize();
    }
}
