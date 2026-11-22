using System.Net;
using FluxFlow.Execution.Plugins;
using FluxFlow.Plugins.Management;
using FluxFlow.Environments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Execution;

public static class LocalCompanionEndpoints
{
    public static IServiceCollection AddLocalFlowRunner(this IServiceCollection services, Plugins.InstalledPlugins? plugins = null)
    {
        if (plugins is null) services.AddFlowEnvironments();
        else
        {
            services.AddBaseFlowEnvironments();
            services.AddSingleton(_ => plugins);
            if (plugins.Manager is { } manager) services.AddSingleton<IPluginManager>(manager);
            plugins.ConfigureHost(services);
        }
        services.AddLogging(logging => logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("FluxFlow.Execution"));
            options.AddProcessor(new ExecutionLogProcessor());
        }));
        services.AddHttpClient<IRegistryAccess, DockerRegistryAccess>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<ExecutionTraceCollector>();
        services.AddSingleton<WorkflowExecution>();
        services.AddSingleton<LocalEnvironmentRunner>();
        services.AddSingleton<IEnvironmentRunner>(provider => provider.GetRequiredService<LocalEnvironmentRunner>());
        services.AddHostedService(provider => provider.GetRequiredService<LocalEnvironmentRunner>());
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, EnvironmentJsonContext.Default));
        return services;
    }

    public static void MapLocalFlowRunner(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/runs");
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var request = http.Request;
            if (http.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote)) return Results.StatusCode(403);
            if (!Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var address) || !address.IsLoopback) return Results.StatusCode(403);
            if (request.Headers.Origin.Count != 0 && !string.Equals(request.Headers.Origin, address.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)) return Results.StatusCode(403);
            if (request.Headers["X-FluxFlow-Run"] != "1") return Results.StatusCode(403);
            return await next(context);
        });
        var plugins = endpoints.ServiceProvider.GetService<Plugins.InstalledPlugins>();
        if (plugins?.Manager is not null) group.MapPluginManagement();
        group.MapGet("/plugins", () => Results.Json(plugins?.Descriptions ?? [], FluxFlow.Plugins.PluginJsonContext.Default.PluginDescriptionArray));
        foreach (var extension in endpoints.ServiceProvider.GetServices<IPluginEndpoints>()) extension.Map(group);
        group.MapGet("/plugins/{id}/studio", (string id, string? dependency) => plugins?.StudioAssembly(id, browser: true, dependency: dependency) is { } file
            ? Results.File(file, "application/octet-stream") : Results.NotFound());
        group.MapPost("/plugins/{id}/suggestions/{operation}", async (string id, string operation, HttpContext http, CancellationToken cancellation) =>
        {
            var list = http.RequestServices.GetKeyedService<Func<CancellationToken, Task<string[]>>>(new FluxFlow.Plugins.SuggestionKey(id, operation));
            if (list is null) return Results.NotFound();
            try { return Results.Json(await list(cancellation), FluxFlow.Plugins.PluginJsonContext.Default.StringArray); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            { return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
        });
        group.MapPost("/check", async (EnvironmentRunRequest request, IEnvironmentRunner runner, CancellationToken cancellation) =>
            Results.Json(await runner.CheckAsync(request, cancellation), EnvironmentJsonContext.Default.EnvironmentCheckResult))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(4 * 1024 * 1024));
        group.MapPost("/{id:guid}", async (Guid id, EnvironmentRunRequest request, IEnvironmentRunner runner, CancellationToken cancellation) =>
            Results.Json(await runner.StartAsync(id, request, cancellation), EnvironmentJsonContext.Default.EnvironmentRunStatus))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(4 * 1024 * 1024));
        group.MapGet("/{id:guid}", async (Guid id, IEnvironmentRunner runner, CancellationToken cancellation) =>
            Results.Json(await runner.GetAsync(id, cancellation), EnvironmentJsonContext.Default.EnvironmentRunStatus));
        // Query retained OTLP payloads; these are read endpoints, not OTLP ingestion endpoints.
        group.MapGet("/{id:guid}/telemetry/logs", (Guid id, LocalEnvironmentRunner runner, HttpContext http) =>
            OtlpResult(http, runner.ReadLogs(id, false)));
        group.MapGet("/{id:guid}/telemetry/diagnostics", (Guid id, LocalEnvironmentRunner runner, HttpContext http) =>
            OtlpResult(http, runner.ReadLogs(id, true)));
        group.MapGet("/{id:guid}/telemetry/traces", (Guid id, LocalEnvironmentRunner runner, HttpContext http) =>
            OtlpResult(http, runner.ReadTraces(id)));
        group.MapDelete("/{id:guid}", async (Guid id, IEnvironmentRunner runner) =>
        {
            await runner.StopAsync(id, CancellationToken.None);
            return Results.NoContent();
        });
        group.MapPost("/registries/test", async (ImageRegistry registry, IRegistryAccess access, CancellationToken cancellation) =>
            Results.Json(await access.TestAsync(registry, cancellation), EnvironmentJsonContext.Default.RegistryAccessResult));
        group.MapPost("/registries/sign-in", async (RegistrySignIn request, IRegistryAccess access, CancellationToken cancellation) =>
            Results.Json(await access.SignInAsync(request, cancellation), EnvironmentJsonContext.Default.RegistryAccessResult))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16 * 1024));
        group.MapPost("/registries/sign-out", async (ImageRegistry registry, IRegistryAccess access, CancellationToken cancellation) =>
            Results.Json(await access.SignOutAsync(registry, cancellation), EnvironmentJsonContext.Default.RegistryAccessResult));
    }

    private static IResult OtlpResult(HttpContext http, (byte[] Content, long Dropped) snapshot)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-FluxFlow-Dropped"] = snapshot.Dropped.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Bytes(snapshot.Content, "application/x-protobuf");
    }
}
