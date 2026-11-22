using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Conversations.Dictation;
using FluxFlow.Studio.UI.Features.Chat.Dictation;
using Microsoft.JSInterop;

namespace FluxFlow.DesignerApp.Services;

public sealed class BrowserDictationService(IJSRuntime js, HttpClient http) : IDictationService
{
    private string? _session;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_session is not null) throw new InvalidOperationException("Dictation is already active.");
        cancellationToken.ThrowIfCancellationRequested();
        var session = Guid.NewGuid().ToString("N");
        _session = session;
        try
        {
            await js.InvokeVoidAsync("studioDictation.start", session);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch { await CancelSession(session); throw; }
    }

    public async Task<string> StopAsync(DictationSettings settings, CancellationToken cancellationToken)
    {
        var session = _session ?? throw new InvalidOperationException("Start dictation first.");
        try
        {
            var json = await js.InvokeAsync<string>("studioDictation.stop", cancellationToken, session);
            cancellationToken.ThrowIfCancellationRequested();
            var recording = JsonSerializer.Deserialize(json, RecordingJsonContext.Default.BrowserRecording)
                ?? throw new InvalidOperationException("No audio was recorded.");
            var bytes = Convert.FromBase64String(recording.Data);
            using var form = new MultipartFormDataContent();
            using var audio = new ByteArrayContent(bytes);
            audio.Headers.ContentType = MediaTypeHeaderValue.Parse(recording.MediaType);
            form.Add(audio, "Audio", "recording");
            form.Add(new StringContent(settings.Location), "Location");
            form.Add(new StringContent(settings.Model), "Model");
            form.Add(new StringContent(settings.Language), "Language");
            using var response = await http.PostAsync("api/dictation/transcribe", form, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string? detail = null;
                if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
                {
                    using var problem = JsonDocument.Parse(text);
                    if (problem.RootElement.TryGetProperty("detail", out var value)) detail = value.GetString();
                }
                throw new InvalidOperationException(detail ?? $"Dictation failed (HTTP {(int)response.StatusCode}). Check transcription settings.");
            }
            return text;
        }
        finally { await CancelSession(session); }
    }

    public ValueTask CancelAsync() => _session is { } session ? CancelSession(session) : ValueTask.CompletedTask;
    private async ValueTask CancelSession(string session)
    {
        if (_session == session) _session = null;
        try { await js.InvokeVoidAsync("studioDictation.cancel", session); }
        catch (Exception error) when (error is JSException or JSDisconnectedException or TaskCanceledException) { }
    }
}

internal sealed record BrowserRecording(string Data, string MediaType);
[JsonSerializable(typeof(BrowserRecording))]
internal partial class RecordingJsonContext : JsonSerializerContext;
