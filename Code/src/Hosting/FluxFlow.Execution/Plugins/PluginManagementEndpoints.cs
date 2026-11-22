using FluxFlow.Plugins.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FluxFlow.Execution.Plugins;

internal static class PluginManagementEndpoints
{
    internal static void MapPluginManagement(this RouteGroupBuilder runs)
    {
        var group = runs.MapGroup("/plugins/manage");
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
            { return Results.Text(e.Message, statusCode: StatusCodes.Status400BadRequest); }
        });
        group.MapGet("/", async (IPluginManager manager) =>
            Results.Json(await manager.ListAsync(), PluginManagementJson.Default.PluginEntryArray));
        group.MapGet("/sources", async (IPluginManager manager) =>
            Results.Json(await manager.GetSourcesAsync(), PluginManagementJson.Default.PluginSourceArray));
        group.MapPost("/sources", async (HttpRequest request, IPluginManager manager) =>
        {
            var change = await request.ReadFromJsonAsync(PluginManagementJson.Default.PluginSourceChange);
            await manager.SaveSourcesAsync(change ?? throw new ArgumentException("Missing source settings."));
            return Results.NoContent();
        });
        group.MapPost("/sources/folder", async (HttpRequest request, IPluginManager manager) =>
        {
            var source = await request.ReadFromJsonAsync(PluginManagementJson.Default.PluginSource);
            return Results.Json(await manager.ListFolderAsync(source ?? throw new ArgumentException("Choose a folder source.")),
                PluginManagementJson.Default.StringArray);
        });
        group.MapPost("/install", async (HttpRequest request, IPluginManager manager) =>
        {
            var path = await request.ReadFromJsonAsync(PluginManagementJson.Default.String);
            await manager.InstallAsync(path ?? "");
            return Results.NoContent();
        });
        group.MapPost("/archive", async (string name, bool? folder, HttpRequest request, IPluginManager manager) =>
        {
            if (folder == true) await manager.InstallFolderArchiveAsync(request.Body);
            else await manager.InstallArchiveAsync(name, request.Body);
            return Results.NoContent();
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(IPluginManager.MaxUploadBytes));
        group.MapPost("/feed", async (HttpRequest request, IPluginManager manager) =>
        {
            var package = await request.ReadFromJsonAsync(PluginManagementJson.Default.PluginPackage);
            await manager.InstallFeedAsync(package ?? throw new ArgumentException("Choose a NuGet package."));
            return Results.NoContent();
        });
        group.MapPost("/remove/{id}", async (string id, IPluginManager manager) =>
        { await manager.RemoveAsync(id); return Results.NoContent(); });
        group.MapPost("/cancel/{id}", async (string id, IPluginManager manager) =>
        { await manager.CancelAsync(id); return Results.NoContent(); });
    }
}
