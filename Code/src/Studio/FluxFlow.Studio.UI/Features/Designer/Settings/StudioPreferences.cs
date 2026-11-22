using System.Text.Json;
using FluxFlow.Studio.UI.Features.Designer.Projects;

namespace FluxFlow.Studio.UI.Features.Designer.Settings;

public sealed class StudioPreferences(IStudioSettingsStorage storage, IEnumerable<FluxFlow.Environments.IResourceEnvironmentAdapter>? adapters = null)
{
    private bool _canWrite = true;
    public StudioSettings Value { get; private set; } = new();
    public bool IsSaving { get; private set; }
    public string? SaveStatus { get; private set; }
    public event Action? Changed;

    public async Task LoadAsync()
    {
        EnsureNotSaving();
        _canWrite = false;
        var json = await storage.ReadAsync();
        if (json is not null)
        {
            var settings = JsonSerializer.Deserialize(json, StudioJsonContext.Default.StudioSettings)
                ?? throw new JsonException("Studio settings must be an object.");
            settings = settings with { Environments = settings.Environments?.WithExplicitRegistries()! };
            settings.Validate(adapters);
            Value = settings;
        }
        _canWrite = true;
        if (json is null) return;
        Changed?.Invoke();
    }

    public async Task SaveAsync(StudioSettings settings)
    {
        EnsureNotSaving();
        if (!_canWrite)
            throw new InvalidOperationException("Reload the existing Studio settings successfully before saving changes.");
        settings.Validate(adapters);
        IsSaving = true;
        SaveStatus = "Saving…";
        Changed?.Invoke();
        try
        {
            await storage.WriteAsync(JsonSerializer.Serialize(settings, StudioJsonContext.Default.StudioSettings));
            Value = settings;
            SaveStatus = "Saved";
        }
        catch
        {
            SaveStatus = "Settings could not be saved";
            throw;
        }
        finally
        {
            IsSaving = false;
            Changed?.Invoke();
        }
    }

    private void EnsureNotSaving()
    {
        if (IsSaving) throw new InvalidOperationException("Settings are being saved. Try again when the save finishes.");
    }
}
