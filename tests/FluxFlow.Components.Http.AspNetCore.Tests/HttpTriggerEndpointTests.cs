using FluxFlow.Components.Http.AspNetCore;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.RequestReply;
using FluxFlow.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using System.Net;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Http.AspNetCore.Tests;

// The full loop against a real ASP.NET Core server (TestServer):
// HTTP request -> endpoint -> bridge -> graph handler -> correlated reply -> HTTP response.
public sealed class HttpTriggerEndpointTests
{
    [Fact]
    public async Task Post_RoutesThroughBridgeAndGraph_ReturnsResponse()
    {
        await using var bridge = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>();
        // The "graph": echo "METHOD:body", preserving the correlation id via With().
        var handler = new ActionBlock<FlowMessage<HttpTriggerRequest>>(request =>
            bridge.Responses.Post(request.With(HttpTriggerReply.Text(
                $"{request.Payload.Method}:{Encoding.UTF8.GetString(request.Payload.Body ?? [])}"))));
        bridge.Output.LinkTo(handler);

        using var host = await StartServerAsync(bridge);
        var client = host.GetTestClient();

        var response = await client.PostAsync("/echo", new StringContent("hi"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("POST:hi");
    }

    [Fact]
    public async Task TriggerNode_IsAFlowNode_WithPropagatingFault()
    {
        var requests = new BufferBlock<IRequestContext<HttpTriggerRequest, HttpTriggerReply>>();
        await using var node = new HttpTriggerNode(requests);

        // The trigger is a first-class node: it carries the uniform lifecycle contract.
        node.ShouldBeAssignableTo<IFlowNode>();

        node.Fault(new InvalidOperationException("boom"));
        await Should.ThrowAsync<InvalidOperationException>(
            () => node.Completion.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task NoResponseFromGraph_TimesOut_Returns504()
    {
        // Requests are accepted but never answered, so the bridge times them out.
        await using var bridge = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>(
            new RequestReplyOptions
            {
                Timeout = TimeSpan.FromMilliseconds(300),
                SweepInterval = TimeSpan.FromMilliseconds(100)
            });
        bridge.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage<HttpTriggerRequest>>());

        using var host = await StartServerAsync(bridge);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/echo");

        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
    }

    [Fact]
    public async Task DiComposition_RoutesThroughKeyedTrigger()
    {
        // The DI-first composition: register the trigger by name, map the endpoint by name.
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddFluxFlowHttpTrigger("echo", trigger =>
                    {
                        var handler = new ActionBlock<FlowMessage<HttpTriggerRequest>>(request =>
                            trigger.Responses.Post(request.With(HttpTriggerReply.Text(
                                $"di:{Encoding.UTF8.GetString(request.Payload.Body ?? [])}"))));
                        trigger.Output.LinkTo(handler);
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapFluxFlowTrigger("/echo", "echo"));
                }))
            .StartAsync();
        var client = host.GetTestClient();

        var response = await client.PostAsync("/echo", new StringContent("hi"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("di:hi");
    }

    [Fact]
    public void AddFluxFlowHttpTrigger_RejectsInvalidRegistrationArguments()
    {
        var services = new ServiceCollection();

        var servicesException = Should.Throw<ArgumentNullException>(() =>
            FluxFlowHttpTriggerServiceCollectionExtensions.AddFluxFlowHttpTrigger(
                null!,
                "echo",
                _ => { }));
        var nameException = Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowHttpTrigger(" ", _ => { }));
        var configureException = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowHttpTrigger("echo", null!));

        servicesException.ParamName.ShouldBe("services");
        nameException.ParamName.ShouldBe("name");
        configureException.ParamName.ShouldBe("configure");
    }

    [Fact]
    public async Task MapFluxFlowTrigger_RejectsInvalidEndpointArguments()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        await using var bridge = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>();
        var routeBuilder = new TestEndpointRouteBuilder(provider);

        var endpointsException = Should.Throw<ArgumentNullException>(() =>
            FluxFlowTriggerEndpointExtensions.MapFluxFlowTrigger(null!, "/echo", "echo"));
        var keyedPatternException = Should.Throw<ArgumentException>(() =>
            routeBuilder.MapFluxFlowTrigger(" ", "echo"));
        var keyedNameException = Should.Throw<ArgumentException>(() =>
            routeBuilder.MapFluxFlowTrigger("/echo", " "));
        var directPatternException = Should.Throw<ArgumentException>(() =>
            routeBuilder.MapFluxFlowTrigger(" ", bridge));
        var coordinatorException = Should.Throw<ArgumentNullException>(() =>
            routeBuilder.MapFluxFlowTrigger("/echo", (RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>)null!));

        endpointsException.ParamName.ShouldBe("endpoints");
        keyedPatternException.ParamName.ShouldBe("pattern");
        keyedNameException.ParamName.ShouldBe("name");
        directPatternException.ParamName.ShouldBe("pattern");
        coordinatorException.ParamName.ShouldBe("coordinator");
    }

    [Fact]
    public async Task AddFluxFlowHttpTrigger_RegistersKeyedSourceNodeAndHostedLifetime()
    {
        var services = new ServiceCollection();
        var configured = false;
        services.AddFluxFlowHttpTrigger("echo", _ => configured = true);

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<HttpTriggerSource>("echo").ShouldNotBeNull();
        configured.ShouldBeFalse();

        var lifetime = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await lifetime.StartAsync(CancellationToken.None);

        configured.ShouldBeTrue();
        provider.GetRequiredKeyedService<HttpTriggerNode>("echo").ShouldNotBeNull();

        await lifetime.StopAsync(CancellationToken.None);
        if (lifetime is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }

    [Fact]
    public void HttpTriggerSource_RejectsNonPositiveCapacity()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() => new HttpTriggerSource(0));

        exception.ParamName.ShouldBe("capacity");
    }

    [Fact]
    public async Task FireAndForget_Returns202Accepted_AndPublishesToGraphWithoutWaiting()
    {
        await using var bridge = new RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply>(
            new RequestReplyOptions { Mode = RequestReplyMode.FireAndForget });
        // The "graph" only observes published requests; it never replies.
        var published = new BufferBlock<FlowMessage<HttpTriggerRequest>>();
        bridge.Output.LinkTo(published);

        using var host = await StartServerAsync(bridge);
        var client = host.GetTestClient();

        var response = await client.PostAsync("/echo", new StringContent("payload"));

        // Acked immediately with 202, and the request still reached the graph.
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var request = await published.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Encoding.UTF8.GetString(request.Payload.Body ?? []).ShouldBe("payload");
    }

    private static Task<IHost> StartServerAsync(
        RequestReplyCoordinator<HttpTriggerRequest, HttpTriggerReply> bridge)
        => new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapFluxFlowTrigger("/echo", bridge));
                }))
            .StartAsync();

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
