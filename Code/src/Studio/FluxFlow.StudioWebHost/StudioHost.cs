using FluxFlow.Authoring.Evaluation;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Authoring.Definitions;
using FluxFlow.Conversations;
using FluxFlow.Conversations.Local;
using FluxFlow.StudioWebHost.Dictation;
using FluxFlow.Execution;
using FluxFlow.Environments.Testcontainers;

namespace FluxFlow.StudioWebHost;

public static class StudioHost
{
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5287");
        var plugins = FluxFlow.Execution.Plugins.InstalledPlugins.Load();
        builder.Services.AddLocalFlowRunner(plugins).AddFlowContainers();
        builder.Services.AddSingleton<FluxFlow.Execution.Plugins.IPluginEndpoints, ApplicationPublishEndpoint>();
        builder.Services.AddJsonataExpressionEngine(useAsDefault: true, functions: plugins.Functions).AddCSharpExpressionEngine();
        builder.Services.AddScoped<IExpressionEvaluator, ExpressionEvaluator>();
        builder.Services.AddCatalogComponents(plugins.Descriptions);
        builder.Services.AddLocalConversations();
        builder.Services.AddHttpClient("Dictation", client => { client.Timeout = TimeSpan.FromMinutes(2); client.MaxResponseContentBufferSize = 1024 * 1024; })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
            options.SerializerOptions.DictionaryKeyPolicy = null;
            options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;
            options.SerializerOptions.Converters.Add(new ComponentAddressConverter());
            options.SerializerOptions.Converters.Add(new PortAddressConverter());
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ExpressionEvaluationJsonContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ProposalJsonContext.Default);
        });
        configure?.Invoke(builder);
        var app = builder.Build();
        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();
        app.MapDictation();
        app.MapLocalFlowRunner();
        app.MapPost("/api/expressions/evaluate", (ExpressionEvaluationRequest request, IExpressionEvaluator evaluator, CancellationToken cancellation)
            => evaluator.EvaluateAsync(request, cancellation));
        app.MapPost("/api/proposals/generate", async (ProposalRequest request, IProposalGenerator generator, CancellationToken cancellation)
            => Results.Json(await generator.GenerateAsync(request, cancellation), ProposalJsonContext.Default.ProposalResult))
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(8 * 1024 * 1024));
        app.MapPost("/api/proposals/models", async (ProposalConnectionRequest request, IProposalConnectionService service, CancellationToken cancellation)
            => Results.Json(await service.GetModelsAsync(request.Connection, cancellation), ProposalJsonContext.Default.ProposalConnectionStatus));
        app.MapPost("/api/proposals/stream", async (ProposalRequest request, IProposalGenerator generator, HttpResponse response, CancellationToken cancellation) =>
        {
            response.ContentType = "application/x-ndjson";
            response.Headers.CacheControl = "no-store";
            var result = await generator.GenerateAsync(request, text => Write(new(Text: text)), cancellation);
            await Write(new(Result: result));

            async Task Write(ProposalUpdate update)
            {
                await response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(update, ProposalJsonContext.Default.ProposalUpdate) + "\n", cancellation);
                await response.Body.FlushAsync(cancellation);
            }
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(8 * 1024 * 1024));
        app.MapPost("/api/proposals/test", async (ProposalConnectionRequest request, IProposalConnectionService service, CancellationToken cancellation)
            => Results.Json(await service.TestAsync(request.Connection, cancellation), ProposalJsonContext.Default.ProposalConnectionStatus));
        app.MapFallbackToFile("index.html");
        return app;
    }
}
