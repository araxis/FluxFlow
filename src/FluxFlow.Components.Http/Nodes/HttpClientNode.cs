using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.Http.Nodes;

/// <summary>
/// Sends canonical HTTP requests through a host-owned <see cref="HttpClient"/>
/// and emits response or failure results on one output stream.
/// </summary>
public sealed class HttpClientNode : FlowNode<HttpClientRequest, HttpResponseResult>
{
    public const string RequestCompleted = "http.request.completed";
    public const string RequestFailed = "http.request.failed";

    private readonly HttpClient _httpClient;
    private readonly HttpClientNodeOptions _options;
    private readonly TimeProvider _clock;
    public HttpClientNode(
        HttpClient httpClient,
        HttpClientNodeOptions? options = null,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(options ?? HttpClientNodeOptions.Default))
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = ValidateOptions(options ?? HttpClientNodeOptions.Default);
        _clock = clock ?? TimeProvider.System;
    }

    protected override bool HandlesErrors => true;

    protected override async Task ProcessAsync(FlowMessage<HttpClientRequest> message)
    {
        var result = await ProcessCoreAsync(message).ConfigureAwait(false);
        await EmitAsync(result, Stopping).ConfigureAwait(false);
    }

    private async Task<FlowMessage<HttpResponseResult>> ProcessCoreAsync(
        FlowMessage<HttpClientRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.IsError)
            return message.WithError<HttpResponseResult>(message.Error!);

        var input = message.Value;
        var startedAt = _clock.GetUtcNow();
        var method = NormalizeMethod(input?.Method);
        var url = input?.Url?.Trim() ?? string.Empty;

        if (input is null)
        {
            return CompleteFailure(
                message,
                startedAt,
                method,
                url,
                HttpErrorCodeNames.InvalidUrl,
                "http.client requires an input request.");
        }

        if (input.Timeout is { } requestTimeout && requestTimeout <= TimeSpan.Zero)
        {
            return CompleteFailure(
                message,
                startedAt,
                method,
                url,
                HttpErrorCodeNames.InvalidTimeout,
                "http.client request timeout must be greater than zero.");
        }

        HttpRequestMessage request;
        try
        {
            request = BuildRequest(input);
            method = request.Method.Method;
            url = ResolveUrl(request, input.Url);
        }
        catch (HttpClientInputException exception)
        {
            return CompleteFailure(
                message,
                startedAt,
                method,
                url,
                exception.Code,
                exception.Message,
                exception);
        }

        using (request)
        using (var requestCancellation =
               CancellationTokenSource.CreateLinkedTokenSource(Stopping))
        {
            var timeout = input.Timeout ?? DefaultTimeout(_options);
            if (timeout is { } timeoutValue)
            {
                try
                {
                    requestCancellation.CancelAfter(timeoutValue);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    return CompleteFailure(
                        message,
                        startedAt,
                        method,
                        url,
                        HttpErrorCodeNames.InvalidTimeout,
                        "http.client request timeout exceeds the supported range.",
                        exception);
                }
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception) when (
                requestCancellation.IsCancellationRequested)
            {
                return CompleteFailure(
                    message,
                    startedAt,
                    method,
                    url,
                    HttpErrorCodeNames.Timeout,
                    "http.client request timed out.",
                    exception,
                    isTransient: true);
            }
            catch (OperationCanceledException exception)
            {
                return CompleteFailure(
                    message,
                    startedAt,
                    method,
                    url,
                    HttpErrorCodeNames.Canceled,
                    "http.client request was canceled.",
                    exception,
                    isTransient: true);
            }
            catch (HttpRequestException exception)
            {
                return CompleteFailure(
                    message,
                    startedAt,
                    method,
                    url,
                    HttpErrorCodeNames.Network,
                    $"http.client request failed to reach the server: {exception.Message}",
                    exception,
                    isTransient: true);
            }
            catch (Exception exception)
            {
                return CompleteFailure(
                    message,
                    startedAt,
                    method,
                    url,
                    HttpErrorCodeNames.SendFailed,
                    $"http.client request failed: {exception.Message}",
                    exception);
            }

            using (response)
            {
                byte[] bodyBytes;
                bool truncated;
                try
                {
                    (bodyBytes, truncated) = await ReadBodyAsync(
                            response,
                            requestCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException exception) when (
                    requestCancellation.IsCancellationRequested)
                {
                    return CompleteFailure(
                        message,
                        startedAt,
                        method,
                        url,
                        HttpErrorCodeNames.Timeout,
                        "http.client response read timed out.",
                        exception,
                        isTransient: true);
                }
                catch (Exception exception)
                {
                    return CompleteFailure(
                        message,
                        startedAt,
                        method,
                        url,
                        HttpErrorCodeNames.ResponseReadFailed,
                        $"http.client failed to read the response body: {exception.Message}",
                        exception,
                        isTransient: exception is HttpRequestException);
                }

                var result = BuildResponse(response, method, bodyBytes, truncated, startedAt);
                if (_options.TreatNonSuccessStatusAsError && !result.Success)
                {
                    return CompleteFailure(
                        message,
                        startedAt,
                        result.Method,
                        result.Url,
                        HttpErrorCodeNames.NonSuccessStatus,
                        $"http.client received non-success status {result.StatusCode}.",
                        response: result);
                }

                PublishEvent(
                    message,
                    result,
                    RequestCompleted,
                    FlowEventLevel.Information,
                    $"{result.Method} {result.Url} -> {result.StatusCode}");
                return message.With(result);
            }
        }
    }

    private HttpRequestMessage BuildRequest(HttpClientRequest input)
    {
        HttpRequestMessage request;
        try
        {
            request = new HttpRequestMessage
            {
                Method = new HttpMethod(NormalizeMethod(input.Method))
            };
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new HttpClientInputException(
                HttpErrorCodeNames.InvalidMethod,
                $"http.client method '{input.Method}' is invalid.",
                exception);
        }

        if (!string.IsNullOrWhiteSpace(input.Url))
        {
            if (!Uri.TryCreate(input.Url.Trim(), UriKind.RelativeOrAbsolute, out var uri))
            {
                request.Dispose();
                throw new HttpClientInputException(
                    HttpErrorCodeNames.InvalidUrl,
                    $"http.client URL '{input.Url}' is invalid.");
            }

            request.RequestUri = uri;
        }
        else if (_httpClient.BaseAddress is null)
        {
            request.Dispose();
            throw new HttpClientInputException(
                HttpErrorCodeNames.InvalidUrl,
                "http.client input requires a URL when the HttpClient has no BaseAddress.");
        }

        try
        {
            request.Content = BuildContent(input.Body);
            foreach (var header in input.Headers)
            {
                try
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                        request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                catch (Exception exception) when (exception is FormatException or ArgumentException)
                {
                    throw new HttpClientInputException(
                        HttpErrorCodeNames.InvalidHeader,
                        $"http.client header '{header.Key}' is invalid.",
                        exception);
                }
            }

            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static HttpContent? BuildContent(FlowContent? body)
    {
        if (body is null)
            return null;
        var content = new ByteArrayContent(body.Bytes.AsSpan().ToArray());
        if (string.IsNullOrWhiteSpace(body.ContentType))
            return content;

        if (!MediaTypeHeaderValue.TryParse(body.ContentType, out var mediaType))
        {
            content.Dispose();
            throw new HttpClientInputException(
                HttpErrorCodeNames.InvalidContent,
                $"http.client request content type '{body.ContentType}' is invalid.");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(mediaType.CharSet) &&
                !string.IsNullOrWhiteSpace(body.Encoding))
            {
                mediaType.CharSet = body.Encoding;
            }
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            content.Dispose();
            throw new HttpClientInputException(
                HttpErrorCodeNames.InvalidContent,
                $"http.client request encoding '{body.Encoding}' is invalid.",
                exception);
        }

        content.Headers.ContentType = mediaType;
        return content;
    }

    private async Task<(byte[] Bytes, bool Truncated)> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var truncated = false;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var remaining = _options.MaxResponseBodyBytes - (int)buffer.Length;
            if (read > remaining)
            {
                if (remaining > 0)
                    buffer.Write(chunk, 0, remaining);

                truncated = true;
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), truncated);
    }

    private HttpResponseResult BuildResponse(
        HttpResponseMessage response,
        string method,
        byte[] bodyBytes,
        bool truncated,
        DateTimeOffset startedAt)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
            headers[header.Key] = header.Value.ToArray();
        foreach (var header in response.Content.Headers)
            headers[header.Key] = header.Value.ToArray();

        var contentType = response.Content.Headers.ContentType;
        var url = response.RequestMessage?.RequestUri?.ToString()
            ?? _httpClient.BaseAddress?.ToString()
            ?? string.Empty;
        return new HttpResponseResult(
            _clock.GetUtcNow(),
            method,
            url,
            (int)response.StatusCode,
            response.ReasonPhrase,
            headers,
            FlowContent.FromBytes(
                bodyBytes,
                contentType?.ToString(),
                NormalizeEncoding(contentType?.CharSet)),
            Elapsed(startedAt),
            response.IsSuccessStatusCode,
            truncated);
    }

    private FlowMessage<HttpResponseResult> CompleteFailure(
        FlowMessage<HttpClientRequest> source,
        DateTimeOffset startedAt,
        string method,
        string url,
        string code,
        string text,
        Exception? exception = null,
        bool isTransient = false,
        HttpResponseResult? response = null)
    {
        var elapsed = response?.ElapsedMilliseconds ?? Elapsed(startedAt);
        var details = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["method"] = NormalizeOptional(method),
            ["url"] = NormalizeOptional(url),
            ["elapsedMilliseconds"] = elapsed
        };
        if (response is not null)
            details["statusCode"] = response.StatusCode;
        if (exception is not null)
        {
            details["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name;
        }

        var timestamp = _clock.GetUtcNow();
        var error = new DataFlowError(
            code,
            text,
            category: "HTTP",
            isTransient,
            JsonSerializer.SerializeToElement(details));
        PublishFailureEvent(
            source,
            timestamp,
            method,
            url,
            elapsed,
            response?.StatusCode,
            error);
        return source.WithError<HttpResponseResult>(error);
    }

    private void PublishEvent(
        FlowMessage<HttpClientRequest> source,
        HttpResponseResult result,
        string name,
        FlowEventLevel level,
        string text)
    {
        EmitEvent(new FlowEvent
        {
            Timestamp = result.Timestamp,
            CorrelationId = source.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = HttpResultKinds.Response,
                ["isError"] = false,
                ["errorCode"] = null,
                ["method"] = result.Method,
                ["url"] = result.Url,
                ["statusCode"] = result.StatusCode,
                ["elapsedMilliseconds"] = result.ElapsedMilliseconds
            }
        });
    }

    private void PublishFailureEvent(
        FlowMessage<HttpClientRequest> source,
        DateTimeOffset timestamp,
        string method,
        string url,
        long elapsedMilliseconds,
        int? statusCode,
        FlowError error)
        => EmitEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = source.CorrelationId,
            Name = RequestFailed,
            Level = FlowEventLevel.Warning,
            Message = error.Message,
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = HttpResultKinds.Error,
                ["isError"] = true,
                ["errorCode"] = error.Code,
                ["method"] = method,
                ["url"] = url,
                ["statusCode"] = statusCode,
                ["elapsedMilliseconds"] = elapsedMilliseconds
            }
        });

    private static HttpClientNodeOptions ValidateOptions(HttpClientNodeOptions options)
    {
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "BoundedCapacity must be greater than zero.");
        }
        if (options.MaxResponseBodyBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxResponseBodyBytes must be greater than zero.");
        }
        if (options.MaxDegreeOfParallelism <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxDegreeOfParallelism must be greater than zero.");
        }
        if (options.DefaultTimeoutMilliseconds is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DefaultTimeoutMilliseconds must be greater than zero when specified.");
        }

        return options;
    }

    private static FlowNodeOptions CreateNodeOptions(HttpClientNodeOptions options)
    {
        var validated = ValidateOptions(options);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity,
            MaxDegreeOfParallelism = validated.MaxDegreeOfParallelism
        };
    }

    private static string NormalizeMethod(string? method)
        => string.IsNullOrWhiteSpace(method)
            ? HttpMethod.Get.Method
            : method.Trim().ToUpperInvariant();

    private string ResolveUrl(HttpRequestMessage request, string? inputUrl)
    {
        var requestUri = request.RequestUri;
        if (requestUri?.IsAbsoluteUri == true)
            return requestUri.ToString();
        if (_httpClient.BaseAddress is not null && requestUri is not null)
            return new Uri(_httpClient.BaseAddress, requestUri).ToString();
        return requestUri?.ToString() ?? inputUrl?.Trim() ?? string.Empty;
    }

    private static TimeSpan? DefaultTimeout(HttpClientNodeOptions options)
        => options.DefaultTimeoutMilliseconds is { } milliseconds
            ? TimeSpan.FromMilliseconds(milliseconds)
            : null;

    private static string? NormalizeEncoding(string? encoding)
        => string.IsNullOrWhiteSpace(encoding)
            ? null
            : encoding.Trim().Trim('"');

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private long Elapsed(DateTimeOffset startedAt)
        => Math.Max(0, (long)(_clock.GetUtcNow() - startedAt).TotalMilliseconds);

    private sealed class HttpClientInputException(
        string code,
        string message,
        Exception? innerException = null)
        : Exception(message, innerException)
    {
        public string Code { get; } = code;
    }
}
