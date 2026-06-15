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
    private readonly HttpRequestNodeOptions _options;
    private readonly IHttpRequestSender _sender;
    private readonly IHttpClock _clock;
    private readonly ActionBlock<HttpRequestInput> _input;
    private readonly BufferBlock<HttpResponseOutput> _output;
    private readonly BufferBlock<HttpErrorOutput> _errors;
    private bool _disposed;

    internal HttpRequestNode(
        HttpRequestNodeOptions options,
        IHttpRequestSender sender,
        IHttpClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
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
        await Completion.ConfigureAwait(false);
        await _sender.DisposeAsync().ConfigureAwait(false);
    }

    private async Task HandleAsync(HttpRequestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var startedAt = _clock.UtcNow;
        HttpRequestSendContext sendContext;
        try
        {
            sendContext = ResolveSendContext(input);
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

        using var timeout = new CancellationTokenSource(sendContext.Timeout);
        try
        {
            var response = await _sender.SendAsync(sendContext, timeout.Token)
                .ConfigureAwait(false);
            var timing = CaptureTiming(startedAt);
            response = response with
            {
                Timestamp = timing.Timestamp,
                Method = sendContext.Method,
                Url = sendContext.Url.ToString(),
                ElapsedMilliseconds = timing.ElapsedMilliseconds
            };

            await _output.SendAsync(response).ConfigureAwait(false);
            TryEmitDiagnostic(
                response.Success
                    ? HttpDiagnosticNames.RequestSucceeded
                    : HttpDiagnosticNames.RequestFailed,
                response.Success ? FlowDiagnosticLevel.Information : FlowDiagnosticLevel.Warning,
                response.Success
                    ? "http.request received a successful response."
                    : "http.request received a non-success response.",
                attributes: CreateResponseAttributes(response));

            if (_options.TreatNonSuccessStatusAsError && !response.Success)
            {
                await ReportRequestErrorAsync(
                        HttpErrorCodes.NonSuccessStatus,
                        HttpErrorKind.NonSuccessStatus,
                        $"http.request received status code {response.StatusCode}.",
                        input,
                        timing,
                        statusCode: response.StatusCode,
                        reasonPhrase: response.ReasonPhrase,
                        resolvedUrl: response.Url,
                        method: response.Method)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.Timeout,
                    HttpErrorKind.Timeout,
                    "http.request timed out.",
                    input,
                    CaptureTiming(startedAt),
                    exception,
                    resolvedUrl: sendContext.Url.ToString(),
                    method: sendContext.Method)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.Network,
                    HttpErrorKind.Network,
                    $"http.request failed: {exception.Message}",
                    input,
                    CaptureTiming(startedAt),
                    exception,
                    exception.StatusCode.HasValue ? (int)exception.StatusCode.Value : null,
                    resolvedUrl: sendContext.Url.ToString(),
                    method: sendContext.Method)
                .ConfigureAwait(false);
        }
        catch (HttpClientRequestSenderFactory.HttpResponseBodyTooLargeException exception)
        {
            await ReportRequestErrorAsync(
                    HttpErrorCodes.ResponseTooLarge,
                    HttpErrorKind.ResponseTooLarge,
                    exception.Message,
                    input,
                    CaptureTiming(startedAt),
                    exception,
                    resolvedUrl: sendContext.Url.ToString(),
                    method: sendContext.Method)
                .ConfigureAwait(false);
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
                    resolvedUrl: sendContext.Url.ToString(),
                    method: sendContext.Method)
                .ConfigureAwait(false);
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
                    resolvedUrl: sendContext.Url.ToString(),
                    method: sendContext.Method)
                .ConfigureAwait(false);
        }
    }

    private HttpRequestSendContext ResolveSendContext(HttpRequestInput input)
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
        var headers = ResolveHeaders(input);
        return new HttpRequestSendContext
        {
            Input = input,
            Method = method,
            Url = url,
            Headers = headers,
            BodyBytes = ResolveBodyBytes(input),
            BodyText = input.Body,
            ContentType = ResolveContentType(input, headers),
            Timeout = timeout,
            MaxResponseBodyBytes = _options.MaxResponseBodyBytes
        };
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

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl) &&
            Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUrl) &&
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
        if (_options.RestrictToBaseUrlOrigin &&
            !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
            Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUrl) &&
            !IsSameOrigin(resolved, baseUrl))
        {
            throw new HttpRequestNodeException(
                HttpErrorCodes.UrlNotAllowed,
                HttpErrorKind.UrlNotAllowed,
                $"http.request URL '{resolved}' does not match the baseUrl origin.");
        }

        if (_options.AllowedHosts.Count > 0 &&
            !_options.AllowedHosts.Any(entry => MatchesAllowedHost(resolved.Host, entry)))
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
        var value = timeoutMilliseconds ?? _options.DefaultTimeoutMilliseconds;
        if (value <= 0)
        {
            throw new HttpRequestNodeException(
                HttpErrorCodes.InvalidRequest,
                HttpErrorKind.InvalidRequest,
                "http.request timeout must be greater than zero.");
        }

        return TimeSpan.FromMilliseconds(value);
    }

    private Dictionary<string, string> ResolveHeaders(HttpRequestInput input)
    {
        var headers = new Dictionary<string, string>(
            _options.DefaultHeaders,
            StringComparer.OrdinalIgnoreCase);
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

            headers[header.Key] = header.Value;
        }

        return headers;
    }

    private static bool ContainsForbiddenHeaderCharacters(string? value)
        => value?.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0;

    private static string SanitizeHeaderName(string? name)
        => string.Concat((name ?? string.Empty)
            .Where(static character => character is not '\r' and not '\n' and not '\0'));

    private static byte[]? ResolveBodyBytes(HttpRequestInput input)
    {
        if (input.Bytes is { } bytes)
        {
            return bytes;
        }

        return input.Body is null
            ? null
            : Encoding.UTF8.GetBytes(input.Body);
    }

    private static string? ResolveContentType(HttpRequestInput input, IReadOnlyDictionary<string, string> headers)
    {
        if (!string.IsNullOrWhiteSpace(input.ContentType))
        {
            return input.ContentType.Trim();
        }

        return headers.TryGetValue("Content-Type", out var contentType)
            ? contentType
            : null;
    }

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

    private static Dictionary<string, object?> CreateResponseAttributes(HttpResponseOutput response)
        => new(StringComparer.Ordinal)
        {
            ["method"] = response.Method,
            ["url"] = response.Url,
            ["statusCode"] = response.StatusCode,
            ["success"] = response.Success,
            ["elapsedMilliseconds"] = response.ElapsedMilliseconds,
            ["responseBytes"] = response.BodyBytes.Length
        };

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

    private static string CreateErrorContext(HttpErrorOutput error)
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

    private HttpRequestTiming CaptureTiming(DateTimeOffset startedAt)
    {
        var completedAt = _clock.UtcNow;
        return new HttpRequestTiming(
            completedAt,
            HttpClockSupport.GetElapsedMilliseconds(startedAt, completedAt));
    }

    private readonly record struct HttpRequestTiming(
        DateTimeOffset Timestamp,
        long ElapsedMilliseconds);
}
