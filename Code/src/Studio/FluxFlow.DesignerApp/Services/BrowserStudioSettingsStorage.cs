using FluxFlow.Studio.UI.Features.Designer.Settings;
using Microsoft.JSInterop;

namespace FluxFlow.DesignerApp.Services;

public sealed class BrowserStudioSettingsStorage(IJSRuntime js) : IStudioSettingsStorage
{
    private const string StorageKey = "FluxFlow.Studio.Settings";

    public async Task<string?> ReadAsync() =>
        await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);

    public async Task WriteAsync(string json) =>
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
}
