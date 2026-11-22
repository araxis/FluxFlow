using FluxFlow.Publishing;
using FluxFlow.Studio.UI.Features.Designer.Settings;

namespace FluxFlow.Studio.Desktop.Services;

internal sealed class NativeStudioSettingsStorage(string path) : IStudioSettingsStorage
{
    public async Task<string?> ReadAsync() => File.Exists(path) ? await File.ReadAllTextAsync(path) : null;

    public Task WriteAsync(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return AtomicFile.WriteAsync(path, json);
    }
}
