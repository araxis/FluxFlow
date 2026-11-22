using System.Text;
using FluxFlow.Environments;
using FluxFlow.Plugins.Http.WireMock;

namespace FluxFlow.Plugins.Http;

public static class WireMockContainer
{
    public static ContainerServiceDefinition Definition { get; } = new(WireMockMappings.Provider, 80,
        "/__admin/health", binding => WireMockMappings.Validate(binding.Mappings), InitializeAsync);

    private static async Task InitializeAsync(Uri endpoint, ResourceBinding binding, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = endpoint };
        using var content = new StringContent(binding.Mappings, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/__admin/mappings", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
