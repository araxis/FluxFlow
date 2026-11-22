using System.Net;
using Duende.AccessTokenManagement;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Components.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace FluxFlow.Components.Http;

public static class HttpClientWorkflowExtensions
{
    public static IServiceCollection AddHttpConnection(this IServiceCollection registrations, string address, HttpClientConfiguration config)
    {
        var (baseAddress, tokenEndpoint, headers) = HttpConnectionSettings.Read(config);
        ClientCredentialsClientName? oauthClient = tokenEndpoint is null ? null
            : RegisterOAuth(registrations, address, config, config.Authentication!, tokenEndpoint);
        var retry = HttpConnectionSettings.Retry<HttpRetryStrategyOptions>(config.Retry);
        if (retry is not null)
        {
            retry.ShouldRetryAfterHeader = config.Retry!.ShouldRetryAfterHeader;
            if (!config.Retry.RetryUnsafeMethods) retry.DisableForUnsafeHttpMethods();
        }
        return registrations.AddFluxFlowResource(ApplicationAddress.Parse(address), provider =>
        {
            HttpMessageHandler handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = config.PooledConnectionLifetime,
                AllowAutoRedirect = config.AllowAutoRedirect, UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };
            if (oauthClient is { } tokenClient)
                handler = new HttpOAuthHandler(provider.GetRequiredService<IClientCredentialsTokenManager>(), tokenClient)
                { InnerHandler = handler };
            if (retry is not null)
                handler = new ResilienceHandler(new ResiliencePipelineBuilder<HttpResponseMessage>().AddRetry(retry).Build())
                { InnerHandler = handler };
            var client = new System.Net.Http.HttpClient(handler)
            {
                BaseAddress = baseAddress, Timeout = config.Timeout,
                MaxResponseContentBufferSize = config.MaxResponseContentBufferSize
            };
            foreach (var header in headers) client.DefaultRequestHeaders.Add(header.Key, header.Values);
            return client;
        });
    }

    public static WorkflowBuilder HttpClient(this WorkflowBuilder workflow, string name, HttpComponentConfiguration config)
    {
        workflow.Services.AddHttpClientNode(workflow.Address(name), config);
        return workflow;
    }

    public static IServiceCollection AddHttpClientNode(this IServiceCollection registrations, ComponentAddress address,
        HttpComponentConfiguration config)
    {
        config.Validate();
        return registrations.AddComponentFactory(address, provider => new HttpClientComponent(new()
        {
            Client = provider.GetRequiredFlowResource<System.Net.Http.HttpClient>(config.Client, address.WorkflowName),
            OutputCapacity = config.OutputCapacity
        }))
            .AddInputPort<HttpClientComponent>(address, "Input", node => node.Input)
            .AddOutputPort<HttpClientComponent>(address, "Output", node => node.Output)
            .AddEventPort<HttpClientComponent>(address, "Events", node => node.Events);
    }

    private static ClientCredentialsClientName RegisterOAuth(IServiceCollection services, string resourceAddress,
        HttpClientConfiguration resource, HttpAuthenticationConfiguration config, Uri endpoint)
    {
        var clientName = ClientCredentialsClientName.Parse(resourceAddress);
        var clientId = ClientId.Parse(config.ClientId!);
        var secret = ClientSecret.Parse(config.ClientSecret!);
        var scope = Scope.ParseOrDefault(config.Scope);
        var backchannel = resourceAddress + ".oauth";
        services.AddHttpClient(backchannel, client =>
        {
            client.Timeout = resource.Timeout;
            client.MaxResponseContentBufferSize = resource.MaxResponseContentBufferSize;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, PooledConnectionLifetime = resource.PooledConnectionLifetime
        });
        services.AddClientCredentialsTokenManagement().AddClient(clientName, client =>
        {
            client.TokenEndpoint = endpoint; client.ClientId = clientId; client.ClientSecret = secret;
            client.Scope = scope; client.ClientCredentialStyle = config.ClientCredentialStyle;
            client.HttpClientName = backchannel;
        });
        return clientName;
    }
}
