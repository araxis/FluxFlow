namespace FluxFlow.Studio.UI.Features.Designer.Settings;

public interface IStudioSettingsStorage
{
    Task<string?> ReadAsync();
    Task WriteAsync(string json);
}
