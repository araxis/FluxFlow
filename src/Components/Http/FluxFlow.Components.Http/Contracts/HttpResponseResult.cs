using System.Collections.Immutable;
using FluxFlow.Data;

namespace FluxFlow.Components.Http.Contracts;

public sealed record HttpResponseResult
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _headers;

    public HttpResponseResult(
        DateTimeOffset timestamp,
        string method,
        string url,
        int statusCode,
        string? reasonPhrase,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? headers,
        FlowContent body,
        long elapsedMilliseconds,
        bool success,
        bool bodyTruncated)
    {
        Timestamp = timestamp;
        Method = method ?? string.Empty;
        Url = url ?? string.Empty;
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        _headers = CopyHeaders(headers);
        Body = body ?? throw new ArgumentNullException(nameof(body));
        ElapsedMilliseconds = elapsedMilliseconds;
        Success = success;
        BodyTruncated = bodyTruncated;
    }

    public DateTimeOffset Timestamp { get; }
    public string Method { get; }
    public string Url { get; }
    public int StatusCode { get; }
    public string? ReasonPhrase { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers => _headers;
    public FlowContent Body { get; }
    public long ElapsedMilliseconds { get; }
    public bool Success { get; }
    public bool BodyTruncated { get; }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CopyHeaders(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return ImmutableDictionary.Create<string, IReadOnlyList<string>>(
                StringComparer.OrdinalIgnoreCase);
        }

        var builder = ImmutableDictionary.CreateBuilder<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            builder.Add(
                header.Key,
                header.Value is null
                    ? ImmutableArray<string>.Empty
                    : header.Value.ToImmutableArray());
        }
        return builder.ToImmutable();
    }
}
