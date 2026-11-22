using System.Net.Http.Json;
using FluxFlow.Environments;

namespace FluxFlow.DesignerApp.Services;

public sealed class HttpRegistryAccess(HttpClient client) : IRegistryAccess
{
    public Task<RegistryAccessResult> TestAsync(ImageRegistry registry, CancellationToken cancellationToken) =>
        SendAsync("test", JsonContent.Create(registry, EnvironmentJsonContext.Default.ImageRegistry), cancellationToken);
    public Task<RegistryAccessResult> SignInAsync(RegistrySignIn request, CancellationToken cancellationToken) =>
        SendAsync("sign-in", JsonContent.Create(request, EnvironmentJsonContext.Default.RegistrySignIn), cancellationToken);
    public Task<RegistryAccessResult> SignOutAsync(ImageRegistry registry, CancellationToken cancellationToken) =>
        SendAsync("sign-out", JsonContent.Create(registry, EnvironmentJsonContext.Default.ImageRegistry), cancellationToken);
    private async Task<RegistryAccessResult> SendAsync(string operation, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/runs/registries/{operation}") { Content = content };
        request.Headers.Add("X-FluxFlow-Run", "1");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(EnvironmentJsonContext.Default.RegistryAccessResult, cancellationToken)
            ?? throw new InvalidOperationException("The companion returned an empty registry status.");
    }
}
