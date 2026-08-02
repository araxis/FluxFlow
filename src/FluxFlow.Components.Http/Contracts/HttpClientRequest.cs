using System.Collections.Immutable;
using FluxFlow.Data;

namespace FluxFlow.Components.Http.Contracts;

public sealed record HttpClientRequest
{
    private IReadOnlyDictionary<string, string> _headers =
        ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Method { get; init; } = "GET";

    public string? Url { get; init; }

    public IReadOnlyDictionary<string, string> Headers
    {
        get => _headers;
        init => _headers = value is null || value.Count == 0
            ? ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase)
            : value.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public FlowContent? Body { get; init; }

    public TimeSpan? Timeout { get; init; }
}
