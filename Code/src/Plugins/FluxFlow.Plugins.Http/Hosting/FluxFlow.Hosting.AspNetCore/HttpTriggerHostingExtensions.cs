using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using Microsoft.AspNetCore.Hosting;
using FluxFlow.Components.Http;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Configuration.Components;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace FluxFlow.Hosting.AspNetCore;

public static class HttpTriggerHostingExtensions
{
    /// <summary>Registers the http.trigger configuration reader. Uses the host's existing DI provider.</summary>
    public static IServiceCollection AddHttpTriggers(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ComponentRegistration>("http.trigger", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = HttpTriggerConfiguration.Read(section, static route => RoutePatternFactory.Parse(route));

            registrations.AddHttpTriggerNode(address, config);
            registrations.AddLinks(address, config);
            registrations.AddInputLinks(address.Port("Response"), config.Response);
        });
        return services;
    }

    public static WorkflowBuilder HttpTrigger(this WorkflowBuilder workflow, string name, HttpTriggerConfiguration config)
    {
        config.Validate(route => RoutePatternFactory.Parse(route));
        workflow.Services.AddHttpTriggerNode(workflow.Address(name), config);
        return workflow;
    }

    public static IHostBuilder UseFluxFlowHttpTriggers(this IHostBuilder host)
        => host.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0")
            .ConfigureServices(services => services.AddRouting())
            .Configure(app => app.UseRouting().UseEndpoints(endpoints => endpoints.MapFluxFlowHttpTriggers())));

    private static void AddHttpTriggerNode(this IServiceCollection registrations, ComponentAddress address, HttpTriggerConfiguration config)
    {
        registrations.AddComponentFactory(address, provider => new HttpTrigger(config.ShutdownMode, config.OutputCapacity,
            config.MaxPendingRequests, provider.GetService<TimeProvider>()))
            .AddOutputPort<HttpTrigger>(address, "Output", node => node.Output)
            .AddEventPort<HttpTrigger>(address, "Events", node => node.Events)
            .AddInputPort<HttpTrigger>(address, "Response", node => node.Response);
        // Ordinary DI delegates are the endpoint registrations; no separate catalog or server.
        registrations.AddKeyedSingleton<Action<IEndpointRouteBuilder>>(typeof(HttpTrigger), (endpoints) =>
            endpoints.MapMethods(config.Route, config.Methods, (HttpContext context) =>
            {
                HttpTrigger trigger;
                try { trigger = (HttpTrigger)context.RequestServices.GetWorkflowServices(address.WorkflowName).GetRequiredKeyedService<IFlowNode>(address.Value); }
                catch (InvalidOperationException)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    return Task.CompletedTask;
                }
                return HandleAsync(context, trigger, config);
            }).WithName(address.Value));
    }

    /// <summary>Maps configured triggers before host startup. The returned native group accepts endpoint conventions.</summary>
    public static RouteGroupBuilder MapFluxFlowHttpTriggers(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("");
        foreach (var map in endpoints.ServiceProvider.GetKeyedServices<Action<IEndpointRouteBuilder>>(typeof(HttpTrigger)))
            map(group);
        return group;
    }

    private static async Task HandleAsync(HttpContext context, HttpTrigger trigger, HttpTriggerConfiguration config)
    {
        var lifetime = context.RequestServices.GetRequiredService<IHostApplicationLifetime>();
        if (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
        try
        {
            var request = FlowMessage.Create(await HttpTriggerRequestReader.ReadAsync(context.Request,
                config.MaxRequestBodyBytes, stopped.Token).ConfigureAwait(false));
            // Drain applies to workflow requests, not uploads still being read by the host.
            stopped.Token.ThrowIfCancellationRequested();
            var requestToken = config.ShutdownMode == HttpTriggerShutdownMode.Drain ? context.RequestAborted : stopped.Token;
            FlowMessage reply;
            try
            {
                if (config.ResponseMode == HttpTriggerResponseMode.Acknowledge)
                {
                    await trigger.PublishAsync(request, requestToken).ConfigureAwait(false);
                    context.Response.StatusCode = config.AcknowledgementStatusCode;
                    return;
                }
                reply = await trigger.RequestAsync(request, config.ReplyTimeout, requestToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Admission can fail when capacity is exhausted or the trigger is not running.
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            if (reply.IsError) { context.Response.StatusCode = StatusCodes.Status500InternalServerError; return; }
            var response = HttpReply.Read(reply.Value);
            foreach (var header in response.Headers)
            {
                if (header.Key.Equals(HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals(HeaderNames.TransferEncoding, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Response framing is managed by ASP.NET Core.");
                context.Response.Headers[header.Key] = header.Value;
            }
            context.Response.StatusCode = response.StatusCode;
            if (response.ContentType is not null) context.Response.ContentType = response.ContentType;
            if (response.Body is { } body && response.StatusCode is not (204 or 304))
            {
                var json = body.Json;
                var text = json.ValueKind == System.Text.Json.JsonValueKind.String;
                var contentType = context.Response.ContentType
                    ?? (text ? "text/plain; charset=utf-8" : "application/json; charset=utf-8");
                await Results.Text(text ? json.GetString()! : json.GetRawText(), contentType,
                    statusCode: response.StatusCode).ExecuteAsync(context).ConfigureAwait(false);
            }
        }
        catch (BadHttpRequestException error) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = error.StatusCode;
        }
        catch (TimeoutException) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        }
        catch (OperationCanceledException) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
    }
}
