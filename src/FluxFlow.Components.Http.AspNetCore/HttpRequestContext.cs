using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using Microsoft.AspNetCore.Http;

namespace FluxFlow.Components.Http.AspNetCore;

/// <summary>
/// Bridges one ASP.NET Core <see cref="HttpContext"/> to the request/reply bridge:
/// it exposes the request as an <see cref="HttpTriggerRequest"/> and writes the
/// graph's reply (or a failure status) back to the response. <see cref="Completed"/>
/// is the signal the endpoint awaits so the response stays open until the graph answers.
/// </summary>
public sealed class HttpRequestContext : IRequestContext<HttpTriggerRequest, HttpTriggerReply>
{
    private readonly HttpContext _http;
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private HttpRequestContext(HttpContext http, HttpTriggerRequest request, CorrelationId? correlationId)
    {
        _http = http;
        Request = request;
        CorrelationId = correlationId;
    }

    public HttpTriggerRequest Request { get; }

    public CorrelationId? CorrelationId { get; }

    /// <summary>Completes once the response has been written (reply or failure).</summary>
    public Task Completed => _completed.Task;

    public static async Task<HttpRequestContext> CreateAsync(
        HttpContext http,
        string? correlationHeader = null)
    {
        ArgumentNullException.ThrowIfNull(http);

        byte[]? body = null;
        if (http.Request.ContentLength is > 0
            || http.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            using var buffer = new MemoryStream();
            await http.Request.Body.CopyToAsync(buffer, http.RequestAborted).ConfigureAwait(false);
            body = buffer.ToArray();
        }

        var headers = http.Request.Headers.ToDictionary(
            header => header.Key,
            header => header.Value.Select(value => value ?? string.Empty).ToArray(),
            StringComparer.OrdinalIgnoreCase);

        var request = new HttpTriggerRequest
        {
            Method = http.Request.Method,
            Path = http.Request.Path.Value ?? "/",
            QueryString = http.Request.QueryString.HasValue ? http.Request.QueryString.Value : null,
            Headers = headers,
            Body = body,
            ContentType = http.Request.ContentType
        };

        CorrelationId? correlationId = null;
        if (!string.IsNullOrWhiteSpace(correlationHeader)
            && http.Request.Headers.TryGetValue(correlationHeader, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            correlationId = new CorrelationId(value.ToString());
        }

        return new HttpRequestContext(http, request, correlationId);
    }

    public async Task ReplyAsync(HttpTriggerReply reply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reply);
        try
        {
            if (!_http.Response.HasStarted)
            {
                _http.Response.StatusCode = reply.StatusCode;
                foreach (var header in reply.Headers)
                {
                    _http.Response.Headers[header.Key] = header.Value;
                }

                if (!string.IsNullOrWhiteSpace(reply.ContentType))
                {
                    _http.Response.ContentType = reply.ContentType;
                }
            }

            if (reply.Body is { Length: > 0 })
            {
                await _http.Response.Body.WriteAsync(reply.Body, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _completed.TrySetResult();
        }
    }

    public Task AcknowledgeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_http.Response.HasStarted)
            {
                // Accepted into the graph; no graph result is awaited.
                _http.Response.StatusCode = StatusCodes.Status202Accepted;
            }
        }
        catch
        {
            // The response may already be gone (client aborted); nothing more to do.
        }
        finally
        {
            _completed.TrySetResult();
        }

        return Task.CompletedTask;
    }

    public Task FailAsync(Exception error, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            if (!_http.Response.HasStarted)
            {
                _http.Response.StatusCode = error is TimeoutException
                    ? StatusCodes.Status504GatewayTimeout
                    : StatusCodes.Status500InternalServerError;
            }
        }
        catch
        {
            // The response may already be gone (client aborted); nothing more to do.
        }
        finally
        {
            _completed.TrySetResult();
        }

        return Task.CompletedTask;
    }
}
