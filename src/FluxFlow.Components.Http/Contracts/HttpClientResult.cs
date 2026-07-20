using System.Collections.Immutable;
using System.Text.Json.Serialization;
using FluxFlow.Data;

namespace FluxFlow.Components.Http.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
[JsonDerivedType(typeof(HttpResponseResult), HttpResultKinds.Response)]
[JsonDerivedType(typeof(HttpClientFailureResult), HttpResultKinds.Error)]
public abstract record HttpClientResult : IFlowResult
{
    protected HttpClientResult(
        string kind,
        DateTimeOffset timestamp,
        string method,
        string url,
        long elapsedMilliseconds,
        FlowError? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind.Trim();
        Timestamp = timestamp;
        Method = method ?? string.Empty;
        Url = url ?? string.Empty;
        ElapsedMilliseconds = elapsedMilliseconds;
        Error = error;
    }

    [JsonIgnore]
    public string Kind { get; }

    public DateTimeOffset Timestamp { get; }

    public string Method { get; }

    public string Url { get; }

    public long ElapsedMilliseconds { get; }

    public FlowError? Error { get; }

    public bool IsError => Error is not null;
}

public sealed record HttpResponseResult : HttpClientResult
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
        : base(
            HttpResultKinds.Response,
            timestamp,
            method,
            url,
            elapsedMilliseconds)
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        _headers = CopyHeaders(headers);
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Success = success;
        BodyTruncated = bodyTruncated;
    }

    public int StatusCode { get; }

    public string? ReasonPhrase { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers => _headers;

    public FlowContent Body { get; }

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

public sealed record HttpClientFailureResult : HttpClientResult
{
    public HttpClientFailureResult(
        DateTimeOffset timestamp,
        string method,
        string url,
        long elapsedMilliseconds,
        FlowError error,
        HttpResponseResult? response = null)
        : base(
            HttpResultKinds.Error,
            timestamp,
            method,
            url,
            elapsedMilliseconds,
            error ?? throw new ArgumentNullException(nameof(error)))
    {
        Response = response;
    }

    public HttpResponseResult? Response { get; }
}
