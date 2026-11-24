using System.Text.Json;
using FluxFlow.Execution.Plugins;
using FluxFlow.Studio.Publishing;
using Microsoft.AspNetCore.Http.Features;

namespace FluxFlow.StudioWebHost;

internal sealed class ApplicationPublishEndpoint : IPluginEndpoints
{
    public void Map(IEndpointRouteBuilder routes)
    {
        var limit = new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(4 * 1024 * 1024);
        routes.MapPost("/publish", (HttpRequest request, InstalledPlugins plugins, int startupTimeoutSeconds, CancellationToken ct) =>
            Download(request, "application/zip", "application.zip", (file, configuration, sources) =>
            {
                ApplicationPackage.Write(file, configuration, startupTimeoutSeconds, plugins, sources, ct);
                return Task.CompletedTask;
            }, ct)).WithMetadata(limit);
        routes.MapPost("/generate", (HttpRequest request, InstalledPlugins plugins, CancellationToken ct) =>
            Download(request, "text/plain; charset=utf-8", "App.cs",
                (file, configuration, sources) => ApplicationSource.WriteAsync(file, configuration, plugins, sources, ct), ct)).WithMetadata(limit);
        routes.MapPost("/publish-compiled", (HttpRequest request, InstalledPlugins plugins, int? targetMajor, string? runtime, CancellationToken ct) =>
            Download(request, "application/octet-stream", runtime == "linux-x64" ? "App" : "App.exe",
                (file, configuration, sources) => ApplicationSource.PublishCompiledAsync(file, configuration, plugins, sources, targetMajor ?? 11, runtime ?? "win-x64", ct), ct)).WithMetadata(limit);
        routes.MapPost("/windows-service/{action}", async (string action, string name, int targetMajor, bool automatic,
            HttpRequest request, InstalledPlugins plugins, CancellationToken ct) =>
        {
            try
            {
                var (configuration, sources) = await ReadInputs(request, ct);
                await ApplicationSource.ManageWindowsServiceAsync(action, name, targetMajor, automatic, configuration, plugins, sources, ct);
                return Results.NoContent();
            }
            catch (Exception error) when (IsInputError(error))
            {
                return Results.Text(error.Message, statusCode: 400);
            }
        }).WithMetadata(limit);
    }

    private static async Task<IResult> Download(HttpRequest request, string contentType, string name,
        Func<Stream, string, Dictionary<string, Func<Stream>>?, Task> write, CancellationToken cancellation)
    {
        var file = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        try
        {
            var (configuration, sources) = await ReadInputs(request, cancellation);
            await write(file, configuration, sources);
            file.Position = 0;
            return Results.Stream(file, contentType, name);
        }
        catch (Exception error) when (IsInputError(error))
        {
            file.Dispose();
            return Results.Text(error.Message, statusCode: 400);
        }
        catch { file.Dispose(); throw; }
    }

    private static bool IsInputError(Exception error) => error is JsonException or FormatException or InvalidOperationException
        or ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException;

    private static async Task<(string, Dictionary<string, Func<Stream>>?)> ReadInputs(HttpRequest request, CancellationToken cancellation)
    {
        string configuration;
        Dictionary<string, Func<Stream>>? sources = null;
        if (request.HasFormContentType)
        {
            if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = FormOptions.DefaultMultipartBodyLengthLimit;
            var form = await request.ReadFormAsync(cancellation);
            var export = form.Files.GetFile("configuration") ?? throw new ArgumentException("Supply the application configuration.");
            if (export.Length > 4 * 1024 * 1024) throw new ArgumentException("Application configuration exceeds 4 MB.");
            using var reader = new StreamReader(export.OpenReadStream());
            configuration = await reader.ReadToEndAsync(cancellation);
            sources = new(StringComparer.Ordinal);
            foreach (var upload in form.Files)
            {
                if (ReferenceEquals(upload, export)) continue;
                if (!upload.Name.StartsWith("source:", StringComparison.Ordinal) ||
                    !sources.TryAdd(Uri.UnescapeDataString(upload.Name[7..]), upload.OpenReadStream))
                    throw new ArgumentException("Unexpected or duplicate Source file.");
            }
        }
        else
        {
            using var reader = new StreamReader(request.Body);
            configuration = await reader.ReadToEndAsync(cancellation);
        }
        return (configuration, sources);
    }
}
