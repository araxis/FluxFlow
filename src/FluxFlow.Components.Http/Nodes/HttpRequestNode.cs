using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Diagnostics;
using FluxFlow.Components.Http.Options;
using FluxFlow.Components.Http.Timing;
using FluxFlow.Engine.Components;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Http.Nodes;

public sealed class HttpRequestNode : FlowNodeBase, IAsyncDisposable
{
    private const string NotConnectedMessage =
        "http.request is not connected; establish the http.client (host ConnectAsync) before sending.";

    private readonly HttpRequestNodeOptions _options;
    private readonly IHttpClientHandle _client;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<HttpRequestInput> _input;
    private readonly BufferBlock<HttpResponseOutput> _output;
    private readonly BufferBlock<HttpErrorOutput> _errors;
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private bool _disposed;

    internal HttpRequestNode(
        HttpRequestNodeOptions options,
        IHttpClientHandle client,
        TimeProvider clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "HTTP request bounded capacity must be greater than zero.");
        }

        var blockOptions = new DataflowBlockOptions
        {
            BoundedCapacity = options.BoundedCapacity
        };
        _input = new ActionBlock<HttpRequestInput>(
            HandleAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _output = new BufferBlock<HttpResponseOutput>(blockOptions);
        _errors = new BufferBlock<HttpErrorOutput>(blockOptions);
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
            // Completion may surface a fault; teardown must still run.
        }

        _lifecycleCancellation.Cancel();
        _lifecycleCancellation.Dispose();
    }

    private async Task HandleAsync(HttpRequestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var startedAt = _clock.GetUtcNow();
        ResolvedRequest request;
        try
        {
            // All security validation runs first: method/url shape, the baseUrl
            // origin guard and the allowedHosts allow-list (EnsureUrlAllowed), the
            // timeout, and header injection checks — BEFORE any sender is touched.
            request = ResolveRequest(input);
        }
        catch (HttpRequestNodeException exception)
        {
            await ReportRequestErrorAsync(
                    exception.Code,
                    exception.Kind,
                    exception.Message,
                    input,
                    CaptureTiming(startedAt),
                    exception.InnerException)
                .ConfigureAwait(false);
            return;
        }

        // Borrow the sender the http.client node established. The request node
        // never creates or disposes a sender; if none is established the request
        // reports not connected.
        if (!_client.TryGetSender(out var sender))
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.RequestNotConnected,
                    HttpErrorKind.NotConnected,
                    NotConnectedMessage,
                    input,
                    CaptureTiming(startedAt),
                    resolvedUrl: request.Url.ToString(),
                    method: request.Method)
                .ConfigureAwait(false);
            return;
        }

        await SendAsync(input, request, sender, startedAt).ConfigureAwait(false);
    }

    private async Task SendAsync(
        HttpRequestInput input,
        ResolvedRequest request,
        IHttpRequestSender sender,
        DateTimeOffset startedAt)
    {
        var sendContext = BuildSendContext(input, request);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifecycleCancellation.Token);
        requestCancellation.CancelAfter(request.Timeout);

        HttpResponseOutput response;
        try
        {
            response = await sender.SendAsync(sendContext, requestCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (HttpClientRequestSenderFactory.HttpResponseBodyTooLargeException exception)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.ResponseTooLarge,
                    HttpErrorKind.ResponseTooLarge,
                    $"http.request response body exceeded {exception.MaxBytes} bytes.",
                    input,
                    CaptureTiming(startedAt),
                    exception,
                    resolvedUrl: request.Url.ToString(),
                    method: request.Method)
                .ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException exception) when (
            requestCancellation.IsCancellationRequested &&
            !_lifecycleCancellation.IsCancellationRequested)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.Timeout,
                    HttpErrorKind.Timeout,
                    $"http.request timed out after {request.Timeout.TotalMilliseconds:0} ms.",
                    input,
                    CaptureTiming(startedAt),
                    exception,
                    resolvedUrl: request.Url.ToString(),
                    method: request.Method)
                .ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException exception)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.Canceled,
                    HttpErrorKind.Canceled,
                    "http.request was canceled.",
                    input,
                    CaptureTiming(startedAt),
                    exception,
                    resolvedUrl: request.Url.ToString(),
                    method: request.Method)
                .ConfigureAwait(false);
            return;
        }
        catch (HttpRequestException exception)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.Network,
                    HttpErrorKind.Network,
                    $"http.request failed to reach the server: {exception.Message}",
                    input,
                    CaptureTiming(startedAt),
                    exception,
                    resolvedUrl: request.Url.ToString(),
                    method: request.Method)
                .ConfigureAwait(false);
            return;
        }
        catch (Exception exception)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.SendFailed,
                    HttpErrorKind.SendFailed,
                    $"http.request failed: {exception.Message}",
                    input,
                    CaptureTiming(startedAt),
                    exception,
                    resolvedUrl: request.Url.ToString(),
                    method: request.Method)
                .ConfigureAwait(false);
            return;
        }

        if (_options.TreatNonSuccessStatusAsError && !response.Success)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.NonSuccessStatus,
                    HttpErrorKind.NonSuccessStatus,
                    $"http.request received non-success status {response.StatusCode}.",
                    input,
                    CaptureTiming(startedAt),
                    statusCode: response.StatusCode,
                    reasonPhrase: response.ReasonPhrase,
                    resolvedUrl: response.Url,
                    method: response.Method)
                .ConfigureAwait(false);
            return;
        }

        await _output.SendAsync(response).ConfigureAwait(false);
        TryEmitDiagnostic(
            HttpDiagnosticNames.RequestSucceeded,
            FlowDiagnosticLevel.Information,
            $"http.request {response.Method} {response.Url} -> {response.StatusCode}.",
            attributes: CreateResponseAttributes(response));
    }

    private HttpRequestSendContext BuildSendContext(
        HttpRequestInput input,
        ResolvedRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in input.Headers)
        {
            headers[header.Key] = header.Value;
        }

        var contentType = !string.IsNullOrWhiteSpace(input.ContentType)
            ? input.ContentType
            : (headers.TryGetValue("Content-Type", out var headerContentType)
                ? headerContentType
                : null);

        var bodyBytes = ResolveBodyBytes(input);

        return new HttpRequestSendContext
        {
            Input = input,
            Method = request.Method,
            Url = request.Url,
            Headers = headers,
            BodyBytes = bodyBytes,
            BodyText = input.Body,
            ContentType = contentType,
            Timeout = request.Timeout,
            MaxResponseBodyBytes = _options.MaxResponseBodyBytes
        };
    }

    private static byte[]? ResolveBodyBytes(HttpRequestInput input)
    {
        if (input.Bytes is not null)
        {
            return input.Bytes;
        }

        return input.Body is not null
            ? Encoding.UTF8.GetBytes(input.Body)
            : null;
    }

    private ResolvedRequest ResolveRequest(HttpRequestInput input)
    {
        var method = string.IsNullOrWhiteSpace(input.Method)
            ? "GET"
            : input.Method.Trim().ToUpperInvariant();
        if (!IsToken(method))
        {
            throw new HttpRequestNodeException(
                HttpErrorCodes.InvalidRequest,
                HttpErrorKind.InvalidRequest,
                $"http.request method '{input.Method}' is invalid.");
        }

        var url = ResolveUrl(input.Url);
        var timeout = ResolveTimeout(input.TimeoutMilliseconds);
        ValidateHeaders(input);
        return new ResolvedRequest(method, url, timeout);
    }

    private Uri ResolveUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new HttpRequestNodeException(
                HttpErrorCodes.InvalidUrl,
                HttpErrorKind.InvalidUrl,
                "http.request input requires a URL.");
        }

        var value = url.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return EnsureUrlAllowed(absolute);
        }

        if (!string.IsNullOrWhiteSpace(_client.BaseUrl) &&
            Uri.TryCreate(_client.BaseUrl, UriKind.Absolute, out var baseUrl) &&
            Uri.TryCreate(baseUrl, value, out var combined))
        {
            return EnsureUrlAllowed(combined);
        }

        throw new HttpRequestNodeException(
            HttpErrorCodes.InvalidUrl,
            HttpErrorKind.InvalidUrl,
            $"http.request URL '{url}' is invalid.");
    }

    private Uri EnsureUrlAllowed(Uri resolved)
    {
        if (_client.RestrictToBaseUrlOrigin &&
            !string.IsNullOrWhiteSpace(_client.BaseUrl) &&
            Uri.TryCreate(_client.BaseUrl, UriKind.Absolute, out var baseUrl) &&
            !IsSameOrigin(resolved, baseUrl))
        {
            throw new HttpRequestNodeException(
                HttpErrorCodes.UrlNotAllowed,
                HttpErrorKind.UrlNotAllowed,
                $"http.request URL '{resolved}' does not match the baseUrl origin.");
        }

        if (_client.AllowedHosts.Count > 0 &&
            !_client.AllowedHosts.Any(entry => MatchesAllowedHost(resolved.Host, entry)))
        {
            throw new HttpRequestNodeException(
                HttpErrorCodes.UrlNotAllowed,
                HttpErrorKind.UrlNotAllowed,
                $"http.request URL host '{resolved.Host}' is not in allowedHosts.");
        }

        return resolved;
    }

    private static bool IsSameOrigin(Uri resolved, Uri baseUrl)
        => string.Equals(resolved.Scheme, baseUrl.Scheme, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(resolved.Host, baseUrl.Host, StringComparison.OrdinalIgnoreCase) &&
           resolved.Port == baseUrl.Port;

    private static bool MatchesAllowedHost(string host, string entry)
    {
        var allowed = entry.Trim();
        if (allowed.StartsWith('.'))
        {
            return host.EndsWith(allowed, StringComparison.OrdinalIgnoreCase);
        }

        return host.Equals(allowed, StringComparison.OrdinalIgnoreCase);
    }

    private TimeSpan ResolveTimeout(int? timeoutMilliseconds)
    {
        var value = timeoutMilliseconds ?? _client.DefaultTimeoutMilliseconds;
        if (value <= 0)
        {
            throw new HttpRequestNodeException(
                HttpErrorCodes.InvalidRequest,
                HttpErrorKind.InvalidRequest,
                "http.request timeout must be greater than zero.");
        }

        return TimeSpan.FromMilliseconds(value);
    }

    private void ValidateHeaders(HttpRequestInput input)
    {
        foreach (var header in input.Headers)
        {
            if (ContainsForbiddenHeaderCharacters(header.Key) ||
                ContainsForbiddenHeaderCharacters(header.Value))
            {
                throw new HttpRequestNodeException(
                    HttpErrorCodes.InvalidRequest,
                    HttpErrorKind.InvalidRequest,
                    $"http.request header '{SanitizeHeaderName(header.Key)}' contains forbidden characters.");
            }
        }
    }

    private static bool ContainsForbiddenHeaderCharacters(string? value)
        => value?.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0;

    private static string SanitizeHeaderName(string? name)
        => string.Concat((name ?? string.Empty)
            .Where(static character => character is not '\r' and not '\n' and not '\0'));

    private async Task ReportRequestErrorAsync(
        int code,
        HttpErrorKind kind,
        string message,
        HttpRequestInput input,
        HttpRequestTiming timing,
        Exception? exception = null,
        int? statusCode = null,
        string? reasonPhrase = null,
        string? resolvedUrl = null,
        string? method = null)
    {
        var error = new HttpErrorOutput
        {
            Timestamp = timing.Timestamp,
            Kind = kind,
            Message = message,
            StatusCode = statusCode,
            ReasonPhrase = reasonPhrase,
            Method = method ?? input.Method,
            Url = resolvedUrl ?? input.Url,
            ElapsedMilliseconds = timing.ElapsedMilliseconds
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

    private Dictionary<string, object?> CreateErrorAttributes(HttpErrorOutput error)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = error.Kind.ToString(),
            ["clientName"] = _client.ClientName,
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

    private string CreateErrorContext(HttpErrorOutput error)
    {
        var values = new List<string>
        {
            $"kind={error.Kind}"
        };

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

        values.Add($"clientName={_client.ClientName}");

        return string.Join("; ", values);
    }

    private static bool IsToken(string value)
        => value.Length > 0 &&
           value.All(static character =>
               character > 32 &&
               character < 127 &&
               character is not '(' and not ')' and not '<' and not '>' and not '@' and
                   not ',' and not ';' and not ':' and not '\\' and not '"' and
                   not '/' and not '[' and not ']' and not '?' and not '=' and
                   not '{' and not '}' and not ' ');

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

    private Dictionary<string, object?> CreateResponseAttributes(HttpResponseOutput response)
        => new(StringComparer.Ordinal)
        {
            ["clientName"] = _client.ClientName,
            ["method"] = response.Method,
            ["url"] = response.Url,
            ["statusCode"] = response.StatusCode,
            ["success"] = response.Success,
            ["elapsedMilliseconds"] = response.ElapsedMilliseconds
        };

    private readonly record struct ResolvedRequest(string Method, Uri Url, TimeSpan Timeout);

    private HttpRequestTiming CaptureTiming(DateTimeOffset startedAt)
    {
        var completedAt = _clock.GetUtcNow();
        return new HttpRequestTiming(
            completedAt,
            HttpClockSupport.GetElapsedMilliseconds(startedAt, completedAt));
    }

    private readonly record struct HttpRequestTiming(
        DateTimeOffset Timestamp,
        long ElapsedMilliseconds);
}
