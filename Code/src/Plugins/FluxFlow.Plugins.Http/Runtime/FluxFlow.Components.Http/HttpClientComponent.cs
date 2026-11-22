using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Http;

/// <summary>Sends one request per input. HTTP status codes remain responses; transport failures become errors.</summary>
public sealed class HttpClientComponent : FlowNode
{
    private readonly System.Net.Http.HttpClient _client;
    public ITargetBlock<FlowMessage> Input { get; }

    public HttpClientComponent(HttpClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity);
        _client = options.Client;
        Output = new(static message => message, new() { BoundedCapacity = options.OutputCapacity });
        Events = new(static message => message);
        Lifetime = new FlowLifetime(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, SendAsync, Output, Stopping), new() { BoundedCapacity = 128 });
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private async Task<IEnumerable<FlowMessage>> SendAsync(FlowMessage message)
    {
        var request = message.Value as HttpSendRequest ?? message.Value.Json.Deserialize<HttpSendRequest>(HttpJsonContext.RequestReadOptions)
            ?? throw new JsonException("Expected an HTTP request.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestUrl);
        if (!Enum.IsDefined(request.Encoding) || request.ResponseEncoding is not (HttpBodyEncoding.Text or HttpBodyEncoding.Base64))
            throw new ArgumentException("Request encoding must be Text, Base64 or Json; response encoding must be Text or Base64.");
        if (request.Timeout is { } duration && (duration <= TimeSpan.Zero || duration.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(request.Timeout));
        var address = new Uri(request.RequestUrl, UriKind.RelativeOrAbsolute);
        var resolved = address.IsAbsoluteUri ? address : new Uri(_client.BaseAddress
            ?? throw new ArgumentException("A relative request address requires a client BaseAddress."), address);
        if (resolved.Scheme is not ("http" or "https") || resolved.UserInfo.Length != 0)
            throw new ArgumentException("HTTP requests require an http or https address without embedded credentials.");
        using var outgoing = new HttpRequestMessage(new HttpMethod(request.Method), resolved);
        var mediaType = request.ContentType;
        if (request.Headers.TryGetValue("Content-Type", out var headerContentType))
        {
            if (mediaType is not null) throw new ArgumentException("Specify ContentType or the Content-Type header, not both.");
            mediaType = headerContentType;
        }
        if (request.Form is { } form)
        {
            if (request.Body is not null)
                throw new ArgumentException("Specify Body or Form, not both.");
            if (mediaType is not null)
                throw new ArgumentException("Form sets its own multipart Content-Type and boundary; omit ContentType and the Content-Type header.");
            if (request.Encoding != HttpBodyEncoding.Text)
                throw new ArgumentException("Encoding applies to Body, not Form.");
            outgoing.Content = CreateFormContent(form);
        }
        else if (request.Body is not null)
        {
            var value = request.Body.Json;
            var json = request.Encoding == HttpBodyEncoding.Json || value.ValueKind != JsonValueKind.String;
            if (request.Encoding == HttpBodyEncoding.Base64 && value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Base64 Body must be a string.");
            mediaType ??= json ? "application/json" : null;
            var contentType = mediaType is null ? null : MediaTypeHeaderValue.Parse(mediaType);
            var encoding = contentType?.CharSet is { Length: > 0 } charset && request.Encoding != HttpBodyEncoding.Base64
                ? Encoding.GetEncoding(charset.Trim('"')) : Encoding.UTF8;
            outgoing.Content = request.Encoding == HttpBodyEncoding.Base64
                ? new ByteArrayContent(Convert.FromBase64String(value.GetString()!))
                : new StringContent(json ? value.GetRawText() : value.GetString()!, encoding);
            if (contentType is not null) outgoing.Content.Headers.ContentType = contentType;
        }
        else if (mediaType is not null)
            throw new ArgumentException("ContentType requires a Body; use an empty string for empty content.");
        foreach (var header in request.Headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            try { outgoing.Headers.Add(header.Key, header.Value); }
            catch (InvalidOperationException) when (outgoing.Content is not null)
            {
                outgoing.Content.Headers.Add(header.Key, header.Value);
            }
        }
        using var deadline = request.Timeout is not null ? CancellationTokenSource.CreateLinkedTokenSource(Stopping) : null;
        if (deadline is not null) deadline.CancelAfter(request.Timeout!.Value);
        var cancellationToken = deadline?.Token ?? Stopping;
        // Native buffering applies timeout and size limits through the entire response, including retries.
        using var response = await _client.SendAsync(outgoing, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var body = request.ResponseEncoding == HttpBodyEncoding.Base64
            ? Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false))
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return [message.WithValue(new HttpResponse(response, body, request.ResponseEncoding))];
    }

    private static MultipartFormDataContent CreateFormContent(HttpForm form)
    {
        if (form.Fields.Count == 0 && form.Files.Count == 0)
            throw new ArgumentException("Form requires at least one field or file.");
        var content = new MultipartFormDataContent();
        try
        {
            foreach (var field in form.Fields)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(field.Key);
                AddPart(new StringContent(field.Value, Encoding.UTF8), field.Key);
            }
            foreach (var file in form.Files)
            {
                ArgumentNullException.ThrowIfNull(file);
                ArgumentException.ThrowIfNullOrWhiteSpace(file.Name);
                ArgumentException.ThrowIfNullOrWhiteSpace(file.FilePath);
                var fileName = file.FileName ?? Path.GetFileName(file.FilePath);
                ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
                var mediaType = MediaTypeHeaderValue.Parse(file.ContentType ?? "application/octet-stream");
                // Seekable file streams let native StreamContent rewind for HTTP retries.
                var stream = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                AddPart(new StreamContent(stream), file.Name, fileName, mediaType);
            }
            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }

        void AddPart(HttpContent part, string name, string? fileName = null, MediaTypeHeaderValue? mediaType = null)
        {
            try
            {
                if (mediaType is not null) part.Headers.ContentType = mediaType;
                if (fileName is null) content.Add(part, name);
                else content.Add(part, name, fileName);
            }
            catch
            {
                part.Dispose();
                throw;
            }
        }
    }
}
