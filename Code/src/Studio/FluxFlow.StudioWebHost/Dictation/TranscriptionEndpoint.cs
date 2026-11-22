using System.Net.Http.Headers;
using System.Text.Json;

namespace FluxFlow.StudioWebHost.Dictation;

public static class TranscriptionEndpoint
{
    public const int MaxAudioBytes = 4 * 1024 * 1024;

    public static void MapDictation(this WebApplication app)
    {
        app.MapPost("/api/dictation/transcribe", Transcribe)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxAudioBytes + 64 * 1024));
    }

    private static async Task<IResult> Transcribe(HttpRequest request, IConfiguration configuration, IHttpClientFactory clients, CancellationToken cancellation)
    {
        if (!request.HasFormContentType) return Results.BadRequest("Audio must be a multipart upload.");
        var form = await request.ReadFormAsync(cancellation);
        var location = form["Location"].ToString();
        var model = form["Model"].ToString();
        var language = form.TryGetValue("Language", out var selectedLanguage) ? selectedLanguage.ToString() : "auto";
        if (language is not ("auto" or "fa" or "en"))
            return Results.BadRequest("Choose Auto, Persian or English dictation.");
        if (location is not ("Local" or "Remote") || string.IsNullOrWhiteSpace(model) || model.Length > 200 || model.Any(char.IsControl))
            return Results.BadRequest("Choose a transcription location and model.");
        var file = form.Files.GetFile("Audio");
        if (file is null || file.Length == 0 || file.Length > MaxAudioBytes) return Results.BadRequest("Record between 1 byte and 4 MB of audio.");
        var mediaType = file.ContentType.Split(';')[0];
        var extension = mediaType switch { "audio/wav" => ".wav", "audio/webm" => ".webm", "audio/ogg" => ".ogg", "audio/mp4" => ".mp4", _ => null };
        if (extension is null) return Results.BadRequest("Unsupported recording format.");

        var endpoint = configuration[$"Dictation:{location}:Endpoint"];
        var fixedModel = configuration[$"Dictation:{location}:Model"];
        if (!string.IsNullOrWhiteSpace(fixedModel) && model != fixedModel)
            return Results.BadRequest("The selected model is not available at the configured transcription service.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            (location == "Local" ? !uri.IsLoopback || uri.Scheme is not ("http" or "https") : uri.Scheme != "https"))
            return Results.Problem($"Configure a {(location == "Local" ? "loopback HTTP/HTTPS" : "HTTPS")} transcription endpoint for {location} on the Studio host.", statusCode: 503);
        using var client = clients.CreateClient("Dictation");
        using var upload = new MultipartFormDataContent();
        await using var audio = file.OpenReadStream();
        using var content = new StreamContent(audio);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        upload.Add(content, "file", "recording" + extension);
        upload.Add(new StringContent(model), "model");
        upload.Add(new StringContent("json"), "response_format");
        // Local services accept an explicit auto value; remote services infer language when omitted.
        if (location == "Local" || language != "auto") upload.Add(new StringContent(language), "language");
        using var outgoing = new HttpRequestMessage(HttpMethod.Post, uri) { Content = upload };
        var key = configuration[$"Dictation:{location}:ApiKey"];
        if (!string.IsNullOrWhiteSpace(key)) outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        try
        {
            using var response = await client.SendAsync(outgoing, cancellation);
            if (!response.IsSuccessStatusCode) return Results.Problem($"Transcription service returned HTTP {(int)response.StatusCode}.", statusCode: 502);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            if (result.RootElement.ValueKind != JsonValueKind.Object ||
                !result.RootElement.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                return Results.Problem("Transcription service did not return text.", statusCode: 502);
            return Results.Text(text.GetString() ?? "");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException || error is OperationCanceledException && !cancellation.IsCancellationRequested)
        { return Results.Problem("The configured transcription service is unavailable or returned an invalid response.", statusCode: 502); }
    }
}
