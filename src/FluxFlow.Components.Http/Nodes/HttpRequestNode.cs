using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Diagnostics;
using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Http.Nodes;

public sealed class HttpRequestNode : FlowNodeBase, IAsyncDisposable
{
    private readonly HttpRequestNodeOptions _options;
    private readonly IHttpRequestSender _sender;
    private readonly ActionBlock<HttpRequestInput> _input;
    private readonly BufferBlock<HttpResponseOutput> _output;
    private readonly BufferBlock<HttpErrorOutput> _errors;
    private bool _disposed;

    private HttpRequestNode(
        HttpRequestNodeOptions options,
        IHttpRequestSender sender)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
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

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        HttpComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = HttpOptionsReader.ReadRequestOptions(context.Definition);
        var sender = componentOptions.RequestSenderFactory.Create(new HttpRequestSenderContext
        {
            Address = context.Address,
            Options = options
        }) ?? throw new InvalidOperationException(
            "http.request sender factory returned null.");
        var node = new HttpRequestNode(options, sender);

        return context.CreateNode(node)
            .Input(HttpComponentPorts.Input, node.Input)
            .Output(HttpComponentPorts.Output, node.Output)
            .Output(HttpComponentPorts.Errors, node.RequestErrors)
            .Build();
    }

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

        var stopwatch = Stopwatch.StartNew();
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
                    stopwatch.ElapsedMilliseconds,
                    exception.InnerException)
                .ConfigureAwait(false);
            return;
        }

        using var timeout = new CancellationTokenSource(sendContext.Timeout);
        try
        {
            var response = await _sender.SendAsync(sendContext, timeout.Token)
                .ConfigureAwait(false);
            response = response with
            {
                Method = sendContext.Method,
                Url = sendContext.Url.ToString(),
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
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
                        stopwatch.ElapsedMilliseconds,
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
                    stopwatch.ElapsedMilliseconds,
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
                    stopwatch.ElapsedMilliseconds,
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
                    stopwatch.ElapsedMilliseconds,
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
                    stopwatch.ElapsedMilliseconds,
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
                    stopwatch.ElapsedMilliseconds,
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
            return absolute;
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl) &&
            Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUrl) &&
            Uri.TryCreate(baseUrl, value, out var combined))
        {
            return combined;
        }

        throw new HttpRequestNodeException(
            HttpErrorCodes.InvalidUrl,
            HttpErrorKind.InvalidUrl,
            $"http.request URL '{url}' is invalid.");
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
            headers[header.Key] = header.Value;
        }

        return headers;
    }

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
        long elapsedMilliseconds,
        Exception? exception = null,
        int? statusCode = null,
        string? reasonPhrase = null,
        string? resolvedUrl = null,
        string? method = null)
    {
        var error = new HttpErrorOutput
        {
            Kind = kind,
            Message = message,
            StatusCode = statusCode,
            ReasonPhrase = reasonPhrase,
            Method = method ?? input.Method,
            Url = resolvedUrl ?? input.Url,
            ElapsedMilliseconds = elapsedMilliseconds
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
}
