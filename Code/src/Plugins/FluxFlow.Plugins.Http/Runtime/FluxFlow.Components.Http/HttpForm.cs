using System.Collections.ObjectModel;

namespace FluxFlow.Components.Http;

/// <summary>Multipart form fields and file parts for one HTTP request.</summary>
public sealed class HttpForm
{
    private IReadOnlyDictionary<string, string> _fields = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    private IReadOnlyList<HttpFormFile> _files = Array.Empty<HttpFormFile>();

    public IReadOnlyDictionary<string, string> Fields
    {
        get => _fields;
        init => _fields = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(value, StringComparer.Ordinal));
    }

    public IReadOnlyList<HttpFormFile> Files
    {
        get => _files;
        init => _files = Array.AsReadOnly(value.ToArray());
    }
}
