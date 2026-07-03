using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Nodes;
using System.Net.Http.Headers;
using System.Text;

namespace FluxFlow.Components.Http.Nodes;

/// <summary>
/// A standalone HTTP node — a "blockified" <see cref="HttpClient"/>. Post a
/// <c>FlowMessage&lt;HttpRequestInput&gt;</c> to <c>Input</c>; the node sends it
/// through the injected client and broadcasts a
/// <c>FlowMessage&lt;HttpResponseOutput&gt;</c> on <c>Output</c> carrying the same
/// correlation id (failures on <c>Errors</c>, a note on <c>Events</c>). Works with
/// nothing but <c>new HttpClientNode(httpClient)</c> — no engine. All transport
/// policy (base address, pooling, redirects, default headers, TLS, any
/// allow-list/SSRF handler) lives on the injected client; the node never connects
/// or disposes it.
/// </summary>
public sealed class HttpClientNode : FlowNode<HttpRequestInput, HttpResponseOutput>
{
    public const string RequestSucceeded = "http.request.succeeded";
    public const string RequestFailed = "http.request.failed";

    private readonly HttpClient _httpClient;
    private readonly HttpClientNodeOptions _options;
    private readonly TimeProvider _clock;

    public HttpClientNode(
        HttpClient httpClient,
        HttpClientNodeOptions? options = null,
        TimeProvider? clock = null)
        : this(httpClient, ValidateOptions(options), clock)
    {
    }

    private HttpClientNode(
        HttpClient httpClient,
        ValidatedOptions options,
        TimeProvider? clock)
        : base(options.FlowNodeOptions)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options.HttpClientOptions;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ProcessAsync(FlowMessage<HttpRequestInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var input = message.Payload;
        var startedAt = _clock.GetUtcNow();

        HttpRequestMessage request;
        try
        {
            request = BuildRequest(input);
        }
        catch (InvalidUrlException exception)
        {
            EmitFailure(HttpErrorCodes.InvalidUrl, exception.Message, message, startedAt);
            return;
        }

        using (request)
        using (var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(Stopping))
        {
            var timeout = input.TimeoutMilliseconds ?? _options.DefaultTimeoutMilliseconds;
            if (timeout is { } milliseconds && milliseconds > 0)
            {
                requestCancellation.CancelAfter(TimeSpan.FromMilliseconds(milliseconds));
            }

            var method = request.Method.Method;
            var url = request.RequestUri?.ToString() ?? _httpClient.BaseAddress?.ToString() ?? input.Url;

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (
                requestCancellation.IsCancellationRequested && !Stopping.IsCancellationRequested)
            {
                EmitFailure(HttpErrorCodes.Timeout, "http.client request timed out.", message, startedAt, exception, method: method, url: url);
                return;
            }
            catch (OperationCanceledException exception)
            {
                EmitFailure(HttpErrorCodes.Canceled, "http.client request was canceled.", message, startedAt, exception, method: method, url: url);
                return;
            }
            catch (HttpRequestException exception)
            {
                EmitFailure(HttpErrorCodes.Network, $"http.client request failed to reach the server: {exception.Message}", message, startedAt, exception, method: method, url: url);
                return;
            }
            catch (Exception exception)
            {
                EmitFailure(HttpErrorCodes.SendFailed, $"http.client request failed: {exception.Message}", message, startedAt, exception, method: method, url: url);
                return;
            }

            using (response)
            {
                var (bodyBytes, truncated) = await ReadBodyAsync(response, requestCancellation.Token)
                    .ConfigureAwait(false);
                var output = BuildResponse(response, method, bodyBytes, truncated, startedAt);

                if (_options.TreatNonSuccessStatusAsError && !output.Success)
                {
                    EmitFailure(
                        HttpErrorCodes.NonSuccessStatus,
                        $"http.client received non-success status {output.StatusCode}.",
                        message,
                        startedAt,
                        statusCode: output.StatusCode,
                        method: output.Method,
                        url: output.Url);
                    return;
                }

                // Carry the correlation id forward onto the response.
                Emit(message.With(output));
                EmitEvent(new FlowEvent
                {
                    Timestamp = _clock.GetUtcNow(),
                    CorrelationId = message.CorrelationId,
                    Name = RequestSucceeded,
                    Level = FlowEventLevel.Information,
                    Message = $"{output.Method} {output.Url} -> {output.StatusCode}",
                    Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["method"] = output.Method,
                        ["url"] = output.Url,
                        ["statusCode"] = output.StatusCode,
                        ["success"] = output.Success,
                        ["elapsedMilliseconds"] = output.ElapsedMilliseconds
                    }
                });
            }
        }
    }

    private HttpRequestMessage BuildRequest(HttpRequestInput input)
    {
        var method = string.IsNullOrWhiteSpace(input.Method)
            ? HttpMethod.Get
            : new HttpMethod(input.Method.Trim().ToUpperInvariant());

        var request = new HttpRequestMessage { Method = method };

        if (!string.IsNullOrWhiteSpace(input.Url))
        {
            if (!Uri.TryCreate(input.Url.Trim(), UriKind.RelativeOrAbsolute, out var uri))
            {
                request.Dispose();
                throw new InvalidUrlException($"http.client URL '{input.Url}' is invalid.");
            }

            request.RequestUri = uri;
        }
        else if (_httpClient.BaseAddress is null)
        {
            request.Dispose();
            throw new InvalidUrlException(
                "http.client input requires a URL when the HttpClient has no BaseAddress.");
        }

        var content = BuildContent(input);
        if (content is not null)
        {
            request.Content = content;
        }

        foreach (var header in input.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return request;
    }

    private static HttpContent? BuildContent(HttpRequestInput input)
    {
        HttpContent? content = null;
        if (input.Bytes is not null)
        {
            content = new ByteArrayContent(input.Bytes);
        }
        else if (input.Body is not null)
        {
            content = new StringContent(input.Body, Encoding.UTF8);
        }

        if (content is not null && !string.IsNullOrWhiteSpace(input.ContentType) &&
            MediaTypeHeaderValue.TryParse(input.ContentType, out var mediaType))
        {
            content.Headers.ContentType = mediaType;
        }

        return content;
    }

    private async Task<(byte[] Bytes, bool Truncated)> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var max = _options.MaxResponseBodyBytes;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var truncated = false;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var remaining = max - (int)buffer.Length;
            if (read > remaining)
            {
                if (remaining > 0)
                {
                    buffer.Write(chunk, 0, remaining);
                }

                truncated = true;
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), truncated);
    }

    private HttpResponseOutput BuildResponse(
        HttpResponseMessage response,
        string method,
        byte[] bodyBytes,
        bool truncated,
        DateTimeOffset startedAt)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        var contentType = response.Content.Headers.ContentType?.ToString();

        return new HttpResponseOutput
        {
            Timestamp = _clock.GetUtcNow(),
            Method = method,
            Url = response.RequestMessage?.RequestUri?.ToString()
                ?? _httpClient.BaseAddress?.ToString()
                ?? "",
            StatusCode = (int)response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
            Headers = headers,
            BodyBytes = bodyBytes,
            Body = TryDecodeText(bodyBytes, contentType),
            ContentType = contentType,
            ElapsedMilliseconds = Elapsed(startedAt),
            Success = response.IsSuccessStatusCode,
            BodyTruncated = truncated
        };
    }

    private static string? TryDecodeText(byte[] bodyBytes, string? contentType)
    {
        if (bodyBytes.Length == 0)
        {
            return null;
        }

        var isTextual = contentType is not null &&
            (contentType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("charset", StringComparison.OrdinalIgnoreCase));

        return isTextual ? Encoding.UTF8.GetString(bodyBytes) : null;
    }

    private void EmitFailure(
        int code,
        string message,
        FlowMessage<HttpRequestInput> source,
        DateTimeOffset startedAt,
        Exception? exception = null,
        int? statusCode = null,
        string? method = null,
        string? url = null)
    {
        var elapsed = Elapsed(startedAt);
        var input = source.Payload;
        var context = new List<string>
        {
            $"method={method ?? input.Method}",
            $"url={url ?? input.Url}"
        };
        if (statusCode.HasValue)
        {
            context.Add($"statusCode={statusCode.Value}");
        }

        context.Add($"elapsedMs={elapsed}");

        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = string.Join("; ", context),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = RequestFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = code,
                ["method"] = method ?? input.Method,
                ["url"] = url ?? input.Url,
                ["statusCode"] = statusCode,
                ["elapsedMilliseconds"] = elapsed
            }
        });
    }

    private long Elapsed(DateTimeOffset startedAt)
        => Math.Max(0, (long)(_clock.GetUtcNow() - startedAt).TotalMilliseconds);

    private static ValidatedOptions ValidateOptions(HttpClientNodeOptions? options)
    {
        options ??= HttpClientNodeOptions.Default;
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "BoundedCapacity must be greater than zero.");

        if (options.MaxResponseBodyBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxResponseBodyBytes must be greater than zero.");

        if (options.MaxDegreeOfParallelism <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDegreeOfParallelism must be greater than zero.");

        if (options.DefaultTimeoutMilliseconds is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "DefaultTimeoutMilliseconds must be greater than zero when specified.");

        return new ValidatedOptions(options);
    }

    private sealed class ValidatedOptions(HttpClientNodeOptions httpClientOptions)
    {
        public HttpClientNodeOptions HttpClientOptions { get; } = httpClientOptions;

        public FlowNodeOptions FlowNodeOptions { get; } = new()
        {
            InputCapacity = httpClientOptions.BoundedCapacity,
            MaxDegreeOfParallelism = httpClientOptions.MaxDegreeOfParallelism
        };
    }

    private sealed class InvalidUrlException(string message) : Exception(message);
}
