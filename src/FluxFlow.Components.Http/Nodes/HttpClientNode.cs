using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Diagnostics;
using FluxFlow.Components.Http.Options;
using FluxFlow.Components.Http.Timing;
using FluxFlow.Engine.Components;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Http.Nodes;

/// <summary>
/// A "blockified" <see cref="HttpClient"/>: a request arrives on the input port,
/// it is sent through the injected client, and the response is broadcast on the
/// output port (failures on the error port). The node owns no transport policy —
/// base address, pooling, redirects, default headers, TLS, and any allow-list /
/// SSRF guard all live on the injected <see cref="HttpClient"/> (configured by the
/// host, e.g. via <c>IHttpClientFactory</c> and a delegating handler). The node
/// never disposes the client; the host owns its lifetime.
/// </summary>
public sealed class HttpClientNode : FlowNodeBase, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpClientNodeOptions _options;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<HttpRequestInput> _input;
    private readonly BufferBlock<HttpResponseOutput> _output;
    private readonly BufferBlock<HttpErrorOutput> _errors;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private bool _disposed;

    internal HttpClientNode(
        HttpClient httpClient,
        HttpClientNodeOptions options,
        TimeProvider clock)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        var queueOptions = new DataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity
        };
        _input = new ActionBlock<HttpRequestInput>(
            HandleAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                EnsureOrdered = options.MaxDegreeOfParallelism == 1
            });
        _output = new BufferBlock<HttpResponseOutput>(queueOptions);
        _errors = new BufferBlock<HttpErrorOutput>(queueOptions);
        _input.Completion.ContinueWith(
            CompleteOutputs,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        CompleteWhen(Task.WhenAll(_output.Completion, _errors.Completion));
    }

    public ITargetBlock<HttpRequestInput> Input => _input;

    public ISourceBlock<HttpResponseOutput> Output => _output;

    public ISourceBlock<HttpErrorOutput> RequestErrors => _errors;

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _lifecycleCancellation.Cancel();
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_output).Fault(exception);
            ((IDataflowBlock)_errors).Fault(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Complete();
        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion may surface a fault; teardown must still run. The
            // injected HttpClient is owned by the host and is never disposed here.
        }

        _lifecycleCancellation.Cancel();
        _lifecycleCancellation.Dispose();
    }

    private async Task HandleAsync(HttpRequestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var startedAt = _clock.GetUtcNow();
        HttpRequestMessage request;
        try
        {
            request = BuildRequest(input);
        }
        catch (HttpRequestNodeException exception)
        {
            await ReportRequestErrorAsync(
                    exception.Code,
                    exception.Kind,
                    exception.Message,
                    input,
                    startedAt,
                    exception.InnerException)
                .ConfigureAwait(false);
            return;
        }

        using (request)
        using (var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifecycleCancellation.Token))
        {
            var timeoutMilliseconds = input.TimeoutMilliseconds ?? _options.DefaultTimeoutMilliseconds;
            if (timeoutMilliseconds is { } timeout && timeout > 0)
            {
                requestCancellation.CancelAfter(TimeSpan.FromMilliseconds(timeout));
            }

            await SendAsync(input, request, requestCancellation, startedAt).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(
        HttpRequestInput input,
        HttpRequestMessage request,
        CancellationTokenSource requestCancellation,
        DateTimeOffset startedAt)
    {
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
            requestCancellation.IsCancellationRequested &&
            !_lifecycleCancellation.IsCancellationRequested)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.Timeout,
                    HttpErrorKind.Timeout,
                    "http.client request timed out.",
                    input,
                    startedAt,
                    exception,
                    method: method,
                    resolvedUrl: url)
                .ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException exception)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.Canceled,
                    HttpErrorKind.Canceled,
                    "http.client request was canceled.",
                    input,
                    startedAt,
                    exception,
                    method: method,
                    resolvedUrl: url)
                .ConfigureAwait(false);
            return;
        }
        catch (HttpRequestException exception)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.Network,
                    HttpErrorKind.Network,
                    $"http.client request failed to reach the server: {exception.Message}",
                    input,
                    startedAt,
                    exception,
                    method: method,
                    resolvedUrl: url)
                .ConfigureAwait(false);
            return;
        }
        catch (Exception exception)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.SendFailed,
                    HttpErrorKind.SendFailed,
                    $"http.client request failed: {exception.Message}",
                    input,
                    startedAt,
                    exception,
                    method: method,
                    resolvedUrl: url)
                .ConfigureAwait(false);
            return;
        }

        using (response)
        {
            var (bodyBytes, truncated) = await ReadBodyAsync(response, requestCancellation.Token)
                .ConfigureAwait(false);
            var output = BuildResponse(response, method, bodyBytes, truncated, startedAt);

            if (_options.TreatNonSuccessStatusAsError && !output.Success)
            {
                await ReportRequestErrorAsync(
                        HttpErrorCodes.NonSuccessStatus,
                        HttpErrorKind.NonSuccessStatus,
                        $"http.client received non-success status {output.StatusCode}.",
                        input,
                        startedAt,
                        statusCode: output.StatusCode,
                        reasonPhrase: output.ReasonPhrase,
                        method: output.Method,
                        resolvedUrl: output.Url)
                    .ConfigureAwait(false);
                return;
            }

            await _output.SendAsync(output).ConfigureAwait(false);
            TryEmitDiagnostic(
                HttpDiagnosticNames.RequestSucceeded,
                FlowDiagnosticLevel.Information,
                $"http.client {output.Method} {output.Url} -> {output.StatusCode}.",
                attributes: CreateResponseAttributes(output));
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
                throw new HttpRequestNodeException(
                    HttpErrorCodes.InvalidUrl,
                    HttpErrorKind.InvalidUrl,
                    $"http.client URL '{input.Url}' is invalid.");
            }

            request.RequestUri = uri;
        }
        else if (_httpClient.BaseAddress is null)
        {
            request.Dispose();
            throw new HttpRequestNodeException(
                HttpErrorCodes.InvalidUrl,
                HttpErrorKind.InvalidUrl,
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
        var body = TryDecodeText(bodyBytes, contentType);

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
            Body = body,
            ContentType = contentType,
            ElapsedMilliseconds = CaptureElapsed(startedAt),
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

    private async Task ReportRequestErrorAsync(
        int code,
        HttpErrorKind kind,
        string message,
        HttpRequestInput input,
        DateTimeOffset startedAt,
        Exception? exception = null,
        int? statusCode = null,
        string? reasonPhrase = null,
        string? method = null,
        string? resolvedUrl = null)
    {
        var error = new HttpErrorOutput
        {
            Timestamp = _clock.GetUtcNow(),
            Kind = kind,
            Message = message,
            StatusCode = statusCode,
            ReasonPhrase = reasonPhrase,
            Method = method ?? input.Method,
            Url = resolvedUrl ?? input.Url,
            ElapsedMilliseconds = CaptureElapsed(startedAt)
        };

        await _errors.SendAsync(error).ConfigureAwait(false);
        TryReportError(code, message, exception, CreateErrorContext(error));
        TryEmitDiagnostic(
            HttpDiagnosticNames.RequestFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateErrorAttributes(error));
    }

    private long CaptureElapsed(DateTimeOffset startedAt)
        => HttpClockSupport.GetElapsedMilliseconds(startedAt, _clock.GetUtcNow());

    private static string CreateErrorContext(HttpErrorOutput error)
    {
        var values = new List<string> { $"kind={error.Kind}" };
        if (!string.IsNullOrWhiteSpace(error.Method))
        {
            values.Add($"method={error.Method}");
        }

        if (!string.IsNullOrWhiteSpace(error.Url))
        {
            values.Add($"url={error.Url}");
        }

        if (error.StatusCode.HasValue)
        {
            values.Add($"statusCode={error.StatusCode.Value}");
        }

        return string.Join("; ", values);
    }

    private static Dictionary<string, object?> CreateErrorAttributes(HttpErrorOutput error)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = error.Kind.ToString(),
            ["elapsedMilliseconds"] = error.ElapsedMilliseconds
        };

        if (!string.IsNullOrWhiteSpace(error.Method))
        {
            attributes["method"] = error.Method;
        }

        if (!string.IsNullOrWhiteSpace(error.Url))
        {
            attributes["url"] = error.Url;
        }

        if (error.StatusCode.HasValue)
        {
            attributes["statusCode"] = error.StatusCode.Value;
        }

        return attributes;
    }

    private static Dictionary<string, object?> CreateResponseAttributes(HttpResponseOutput response)
        => new(StringComparer.Ordinal)
        {
            ["method"] = response.Method,
            ["url"] = response.Url,
            ["statusCode"] = response.StatusCode,
            ["success"] = response.Success,
            ["elapsedMilliseconds"] = response.ElapsedMilliseconds
        };

    private void CompleteOutputs(Task completion)
    {
        if (completion.IsFaulted && completion.Exception is { } exception)
        {
            ((IDataflowBlock)_output).Fault(exception);
            ((IDataflowBlock)_errors).Fault(exception);
            return;
        }

        _output.Complete();
        _errors.Complete();
    }

    private sealed class HttpRequestNodeException(
        int code,
        HttpErrorKind kind,
        string message,
        Exception? innerException = null)
        : Exception(message, innerException)
    {
        public int Code { get; } = code;
        public HttpErrorKind Kind { get; } = kind;
    }
}
