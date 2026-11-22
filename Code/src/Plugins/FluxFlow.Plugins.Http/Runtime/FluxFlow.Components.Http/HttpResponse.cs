using System.Collections.ObjectModel;
using System.Text.Json;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Components.Http;

public sealed class HttpResponse : FlowValue
{
    public int StatusCode { get; }
    public string? ReasonPhrase { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
    public string Body { get; }
    public HttpBodyEncoding Encoding { get; }

    internal HttpResponse(HttpResponseMessage response, string body, HttpBodyEncoding encoding)
    {
        StatusCode = (int)response.StatusCode;
        ReasonPhrase = response.ReasonPhrase;
        Headers = new ReadOnlyDictionary<string, IReadOnlyList<string>>(response.Headers.Concat(response.Content.Headers)
            .Concat(response.TrailingHeaders).GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)Array.AsReadOnly(group.SelectMany(header => header.Value).ToArray()),
                StringComparer.OrdinalIgnoreCase));
        Body = body;
        Encoding = encoding;
    }

    protected override JsonElement CreateJson()
        => JsonSerializer.SerializeToElement(this, HttpJsonContext.Default.HttpResponse);
}
