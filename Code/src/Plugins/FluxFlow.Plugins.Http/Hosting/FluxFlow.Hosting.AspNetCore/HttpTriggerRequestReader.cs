using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Core.Messaging;
using Microsoft.AspNetCore.Http;

namespace FluxFlow.Hosting.AspNetCore;

internal static class HttpTriggerRequestReader
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static async Task<FlowValue> ReadAsync(HttpRequest request, int limit, CancellationToken token)
    {
        if (request.ContentLength > limit) throw new BadHttpRequestException("Request body is too large.", 413);
        // Keep the native form parser able to read the same bounded body snapshot.
        if (request.HasFormContentType) request.EnableBuffering();
        using var body = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
            {
                if (body.Length + read > limit) throw new BadHttpRequestException("Request body is too large.", 413);
                body.Write(buffer, 0, read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }

        Dictionary<string, string[]>? form = null;
        List<ReceivedHttpFile>? files = null;
        if (request.HasFormContentType)
        {
            request.Body.Position = 0;
            IFormCollection parsed;
            try
            {
                parsed = await request.ReadFormAsync(token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is InvalidDataException or IOException)
            {
                throw new BadHttpRequestException("Invalid form request body.", 400, error);
            }
            form = parsed.ToDictionary(item => item.Key, item => item.Value.Select(value => value ?? "").ToArray());
            files = new(parsed.Files.Count);
            foreach (var file in parsed.Files)
            {
                var content = new byte[checked((int)file.Length)];
                using var stream = file.OpenReadStream();
                await stream.ReadExactlyAsync(content, token).ConfigureAwait(false);
                files.Add(new(file.Name, file.FileName, file.ContentType, file.Length, content));
            }
        }

        JsonElement value;
        string encoding;
        if (body.Length == 0) { value = JsonFlowValue.Null.Json; encoding = "Text"; }
        else if (request.HasJsonContentType())
        {
            try
            {
                using var document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length));
                value = document.RootElement.Clone();
                encoding = "Json";
            }
            catch (JsonException error) { throw new BadHttpRequestException("Invalid JSON request body.", 400, error); }
        }
        else
        {
            string text;
            try { text = Utf8.GetString(body.GetBuffer(), 0, (int)body.Length); encoding = "Text"; }
            catch (DecoderFallbackException) { text = Convert.ToBase64String(body.GetBuffer(), 0, (int)body.Length); encoding = "Base64"; }
            value = JsonSerializer.SerializeToElement(text, HttpRequestJsonContext.Default.String);
        }
        return new JsonFlowValue(JsonSerializer.SerializeToElement(new ReceivedHttpRequest(
            request.Method, request.PathBase + request.Path, request.ContentType,
            request.Headers.ToDictionary(item => item.Key, item => item.Value.Select(value => value ?? "").ToArray()),
            request.Query.ToDictionary(item => item.Key, item => item.Value.Select(value => value ?? "").ToArray()),
            request.RouteValues.ToDictionary(item => item.Key, item => Convert.ToString(item.Value, System.Globalization.CultureInfo.InvariantCulture)),
            value, encoding, form, files), HttpRequestJsonContext.Default.ReceivedHttpRequest));
    }
}

internal sealed record ReceivedHttpRequest(string Method, string Path, string? ContentType,
    Dictionary<string, string[]> Headers, Dictionary<string, string[]> Query,
    Dictionary<string, string?> RouteValues, JsonElement Body, string Encoding,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, string[]>? Form,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<ReceivedHttpFile>? Files);

internal sealed record ReceivedHttpFile(string Name, string FileName, string ContentType, long Length, byte[] Content);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(ReceivedHttpRequest))]
[JsonSerializable(typeof(string))]
internal sealed partial class HttpRequestJsonContext : JsonSerializerContext;
