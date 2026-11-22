using FluxFlow.Authoring;

namespace FluxFlow.Studio.UI.Features.Observations.Validation;

/// <summary>Transient editor diagnostics; never stored in project configuration or history.</summary>
public sealed class EditorValidationState
{
    private readonly Dictionary<Guid, ValidationMessageModel> _messages = [];
    public IReadOnlyDictionary<Guid, ValidationMessageModel> Messages => _messages;
    public Guid? Selected { get; private set; }
    public event Action? Changed;
    public event Action? RevealRequested;

    public void Set(Guid id, ValidationMessageModel? message)
    {
        if (message is null)
        {
            if (!_messages.Remove(id)) return;
            if (Selected == id) Selected = null;
        }
        else
        {
            if (_messages.TryGetValue(id, out var previous) && previous == message) return;
            _messages[id] = message;
        }
        Changed?.Invoke();
    }

    public void Reveal(Guid id)
    {
        if (!_messages.ContainsKey(id)) return;
        Selected = id;
        RevealRequested?.Invoke();
        Changed?.Invoke();
    }
}
