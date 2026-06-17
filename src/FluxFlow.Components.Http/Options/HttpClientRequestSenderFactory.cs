using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Timing;
using System.Net.Http.Headers;
using System.Text;

namespace FluxFlow.Components.Http.Options;

public sealed class HttpClientRequestSenderFactory : IHttpRequestSenderFactory
{
    public IHttpRequestSender Create(HttpRequestSenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Client);
        ArgumentNullException.ThrowIfNull(context.Clock);

        return Build(context.Client, context.Clock);
    }

    public IHttpRequestSender CreateClient(HttpClientSenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Client);
        ArgumentNullException.ThrowIfNull(context.Clock);

        return Build(context.Client, context.Clock);
    }

    private static IHttpRequestSender Build(IHttpClientHandle client, TimeProvider clock)
    {
        // Security: when an allow-list guard is configured, auto-redirect is
        // disabled so a server cannot 3xx-redirect past the per-request host
        // validation in HttpRequestNode. The redirect response is surfaced
        // as-is instead of being followed.
        var allowAutoRedirect = client.FollowRedirects && !HasAllowListGuard(client);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = allowAutoRedirect
        };

        // Honor the client-scoped pooling configuration that was previously
        // hardcoded. Unset values keep the SocketsHttpHandler defaults.
        if (client.PooledConnectionLifetimeSeconds is { } lifetimeSeconds)
        {
            handler.PooledConnectionLifetime = TimeSpan.FromSeconds(lifetimeSeconds);
        }

        if (client.MaxConnectionsPerServer is { } maxConnections)
        {
            handler.MaxConnectionsPerServer = maxConnections;
        }

        var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        if (!string.IsNullOrWhiteSpace(client.BaseUrl) &&
            Uri.TryCreate(client.BaseUrl, UriKind.Absolute, out var baseAddress))
        {
            httpClient.BaseAddress = baseAddress;
        }

        ApplyDefaultHeaders(httpClient, client.DefaultHeaders);

        return new HttpClientRequestSender(httpClient, clock, allowAutoRedirect);
    }

    private static void ApplyDefaultHeaders(
        HttpClient httpClient,
        IReadOnlyDictionary<string, string> defaultHeaders)
    {
        foreach (var header in defaultHeaders)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                // Content-Type is a content header, not a default request header;
                // it is set per request from the resolved body.
                continue;
            }

            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    // When an allow-list guard is configured (AllowedHosts non-empty OR
    // RestrictToBaseUrlOrigin), auto-redirect is disabled so a server cannot
    // 3xx-redirect past the per-request host validation in HttpRequestNode.
    // The redirect response is surfaced as-is instead of being followed.
    private static bool HasAllowListGuard(IHttpClientHandle client)
        => client.RestrictToBaseUrlOrigin || client.AllowedHosts.Count > 0;

    private sealed class HttpClientRequestSender(
        HttpClient client,
        TimeProvider clock,
        bool allowAutoRedirect) : IHttpRequestSender, IHttpRedirectPolicy
    {
        public bool AllowAutoRedirect => allowAutoRedirect;

        public async Task<HttpResponseOutput> SendAsync(
            HttpRequestSendContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            using var request = CreateRequest(context);
            var startedAt = clock.GetUtcNow();
            using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

            var bodyBytes = await ReadLimitedBodyAsync(
                    response.Content,
                    context.MaxResponseBodyBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString();

            var completedAt = clock.GetUtcNow();
            return new HttpResponseOutput
            {
                Timestamp = completedAt,
                Method = context.Method,
                Url = context.Url.ToString(),
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase,
                Headers = ReadHeaders(response),
                BodyBytes = bodyBytes,
                Body = DecodeBody(bodyBytes, response.Content.Headers.ContentType),
                ContentType = contentType,
                ElapsedMilliseconds = HttpClockSupport.GetElapsedMilliseconds(
                    startedAt,
                    completedAt),
                Success = ((int)response.StatusCode) is >= 200 and <= 299,
                BodyTruncated = false
            };
        }

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }

        private static HttpRequestMessage CreateRequest(HttpRequestSendContext context)
        {
            var request = new HttpRequestMessage(new HttpMethod(context.Method), context.Url);
            if (context.BodyBytes is not null)
            {
                request.Content = new ByteArrayContent(context.BodyBytes);
                if (!string.IsNullOrWhiteSpace(context.ContentType))
                {
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.ContentType);
                }
            }

            foreach (var header in context.Headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value) &&
                    request.Content is not null)
                {
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return request;
        }

        private static async Task<byte[]> ReadLimitedBodyAsync(
            HttpContent content,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return buffer.ToArray();
                }

                if (buffer.Length + read > maxBytes)
                {
                    throw new HttpResponseBodyTooLargeException(maxBytes);
                }

                buffer.Write(chunk, 0, read);
            }
        }

        private static Dictionary<string, string[]> ReadHeaders(HttpResponseMessage response)
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

            return headers;
        }

        private static string? DecodeBody(
            byte[] bodyBytes,
            MediaTypeHeaderValue? contentType)
        {
            if (bodyBytes.Length == 0)
            {
                return string.Empty;
            }

            var mediaType = contentType?.MediaType;
            var charset = contentType?.CharSet?.Trim('"');
            var shouldDecode =
                !string.IsNullOrWhiteSpace(charset) ||
                mediaType?.Contains("text", StringComparison.OrdinalIgnoreCase) == true ||
                mediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ||
                mediaType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true;

            if (!shouldDecode)
            {
                return null;
            }

            var encoding = ResolveEncoding(charset);
            return encoding.GetString(bodyBytes);
        }

        private static Encoding ResolveEncoding(string? charset)
        {
            if (string.IsNullOrWhiteSpace(charset))
            {
                return Encoding.UTF8;
            }

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }
    }

    internal sealed class HttpResponseBodyTooLargeException(int maxBytes)
        : Exception($"HTTP response body exceeded the configured limit of {maxBytes} bytes.")
    {
        public int MaxBytes { get; } = maxBytes;
    }
}
