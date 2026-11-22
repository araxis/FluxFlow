using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluxFlow.Plugins.Management;

namespace FluxFlow.Execution.Plugins;

internal static class NuGetPluginDownload
{
    private static readonly HttpClient Client = new() { MaxResponseContentBufferSize = 1024 * 1024 };

    internal static async Task Install(PluginPackage package, Func<Stream, CancellationToken, Task> stage)
    {
        if (package.Id is not { Length: > 0 and <= 100 } ||
            !Regex.IsMatch(package.Id, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z") ||
            package.Version is not { Length: > 0 and <= 100 } ||
            !Regex.IsMatch(package.Version, @"\A(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*)){2}(-[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*)?\z"))
            throw new ArgumentException("Enter a package ID and exact version, such as 1.0.0 or 1.0.0-beta.1.");

        try
        {
            var index = await Client.GetByteArrayAsync(FeedUri(package.Feed)).ConfigureAwait(false);
            using var document = JsonDocument.Parse(index);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("The feed did not return a NuGet v3 service index.");
            var resource = resources.EnumerateArray().FirstOrDefault(r =>
                r.ValueKind == JsonValueKind.Object && r.TryGetProperty("@type", out var type) && type.ValueKind == JsonValueKind.String &&
                type.GetString() == "PackageBaseAddress/3.0.0");
            if (resource.ValueKind != JsonValueKind.Object || !resource.TryGetProperty("@id", out var address) || address.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("This feed does not offer NuGet package downloads.");
            var root = FeedUri(address.GetString()?.TrimEnd('/') + "/");
            var id = package.Id.ToLowerInvariant();
            var version = package.Version.ToLowerInvariant();
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var response = await Client.GetAsync(new Uri(root, $"{id}/{version}/{id}.{version}.nupkg"),
                HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidDataException("This package ID and version were not found on the selected feed.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > IPluginManager.MaxUploadBytes)
                throw new InvalidDataException("Plugin archive exceeds 64 MiB.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            await stage(stream, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        { throw new InvalidDataException("Could not download the plugin from the selected NuGet feed.", ex); }
    }

    internal static Uri FeedUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            ? uri : throw new ArgumentException("Use an HTTPS NuGet v3 feed URL, or HTTP on localhost.");
}
