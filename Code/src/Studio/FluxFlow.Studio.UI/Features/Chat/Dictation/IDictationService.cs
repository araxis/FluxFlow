using FluxFlow.Conversations.Dictation;

namespace FluxFlow.Studio.UI.Features.Chat.Dictation;

/// <summary>Host-owned microphone capture and transcription. Cancel releases the microphone and pending request.</summary>
public interface IDictationService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task<string> StopAsync(DictationSettings settings, CancellationToken cancellationToken);
    ValueTask CancelAsync();
}

internal sealed class UnavailableDictationService : IDictationService
{
    public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("This host does not provide microphone capture.");
    public Task<string> StopAsync(DictationSettings settings, CancellationToken cancellationToken) => throw new InvalidOperationException("This host does not provide dictation.");
    public ValueTask CancelAsync() => ValueTask.CompletedTask;
}
