using System.Net.Http.Json;
using FluxFlow.Plugins.Management;

namespace FluxFlow.DesignerApp.Services;

internal sealed class HttpPluginManager(HttpClient client) : IPluginManager
{
    public async Task<PluginEntry[]> ListAsync()
    {
        using var response = await Send(HttpMethod.Get, "");
        return await response.Content.ReadFromJsonAsync(PluginManagementJson.Default.PluginEntryArray) ?? [];
    }
    public async Task<PluginSource[]> GetSourcesAsync()
    {
        using var response = await Send(HttpMethod.Get, "sources");
        return await response.Content.ReadFromJsonAsync(PluginManagementJson.Default.PluginSourceArray)
            ?? throw new InvalidDataException("The companion returned no source settings.");
    }

    public Task SaveSourcesAsync(PluginSourceChange change) =>
        Change("sources", JsonContent.Create(change, PluginManagementJson.Default.PluginSourceChange));

    public async Task<string[]> ListFolderAsync(PluginSource source)
    {
        using var response = await Send(HttpMethod.Post, "sources/folder", JsonContent.Create(source, PluginManagementJson.Default.PluginSource));
        return await response.Content.ReadFromJsonAsync(PluginManagementJson.Default.StringArray) ?? [];
    }

    public Task InstallAsync(string path) => Change("install", JsonContent.Create(path, PluginManagementJson.Default.String));
    public Task InstallArchiveAsync(string name, Stream archive) => Change("archive?name=" + Uri.EscapeDataString(name), new StreamContent(archive));
    public Task InstallFolderArchiveAsync(Stream archive) => Change("archive?name=folder.zip&folder=true", new StreamContent(archive));
    public Task InstallFeedAsync(PluginPackage package) => Change("feed", JsonContent.Create(package, PluginManagementJson.Default.PluginPackage));
    public Task RemoveAsync(string id) => Change("remove/" + Uri.EscapeDataString(id));
    public Task CancelAsync(string id) => Change("cancel/" + Uri.EscapeDataString(id));
    private async Task Change(string path, HttpContent? content = null)
    { using var response = await Send(HttpMethod.Post, path, content); }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, "api/runs/plugins/manage/" + path) { Content = content };
        request.Headers.Add("X-FluxFlow-Run", "1");
        var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;
        using (response)
            throw new InvalidOperationException(response.StatusCode == System.Net.HttpStatusCode.BadRequest
                ? await response.Content.ReadAsStringAsync() : $"Plugin management unavailable ({(int)response.StatusCode}).");
    }
}
