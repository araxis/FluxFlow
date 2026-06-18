using System.Text;

namespace FluxFlow.Components.Http.Contracts;

/// <summary>
/// The response a graph produces for an <see cref="HttpTriggerRequest"/>. The trigger
/// adapter writes it back to the caller.
/// </summary>
public sealed record HttpTriggerReply
{
    public int StatusCode { get; init; } = 200;
    public IReadOnlyDictionary<string, string[]> Headers { get; init; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    public byte[]? Body { get; init; }
    public string? ContentType { get; init; }

    public static HttpTriggerReply Status(int statusCode) => new() { StatusCode = statusCode };

    public static HttpTriggerReply Text(
        string body,
        int statusCode = 200,
        string contentType = "text/plain; charset=utf-8")
        => new()
        {
            StatusCode = statusCode,
            Body = Encoding.UTF8.GetBytes(body),
            ContentType = contentType
        };

    public static HttpTriggerReply Json(
        string json,
        int statusCode = 200)
        => new()
        {
            StatusCode = statusCode,
            Body = Encoding.UTF8.GetBytes(json),
            ContentType = "application/json; charset=utf-8"
        };
}
