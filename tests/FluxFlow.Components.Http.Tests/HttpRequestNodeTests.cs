using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Diagnostics;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

public sealed class HttpRequestNodeTests
{
    [Fact]
    public void ClientNode_ExposesConfiguredHandle()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        var resources = HttpResourceTestContext.CreateResources(
            registry,
            new
            {
                baseUrl = "https://example.test/api/",
                allowedHosts = new[] { "example.test", ".internal.example" },
                restrictToBaseUrlOrigin = true,
                followRedirects = false,
                defaultTimeoutMilliseconds = 5000,
                pooledConnectionLifetimeSeconds = 120,
                maxConnectionsPerServer = 8,
                defaultHeaders = new Dictionary<string, string>
                {
                    ["Accept"] = "application/json"
                }
            },
            clientName: "api-client");

        var node = resources[new NodeName("api-client")].Node;
        var handle = node.ShouldBeAssignableTo<IHttpClientHandle>()!;

        handle.ClientName.ShouldBe("api-client");
        handle.BaseUrl.ShouldBe("https://example.test/api/");
        handle.AllowedHosts.ShouldBe(["example.test", ".internal.example"]);
        handle.RestrictToBaseUrlOrigin.ShouldBeTrue();
        handle.FollowRedirects.ShouldBeFalse();
        handle.DefaultTimeoutMilliseconds.ShouldBe(5000);
        handle.PooledConnectionLifetimeSeconds.ShouldBe(120);
        handle.MaxConnectionsPerServer.ShouldBe(8);
        handle.DefaultHeaders["Accept"].ShouldBe("application/json");
    }

    [Fact]
    public void ClientNode_AppliesDefaultsWhenOmitted()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        var resources = HttpResourceTestContext.CreateResources(registry);

        var handle = resources[new NodeName(HttpResourceTestContext.ClientName)].Node
            .ShouldBeAssignableTo<IHttpClientHandle>()!;

        handle.ClientName.ShouldBe(HttpResourceTestContext.ClientName);
        handle.BaseUrl.ShouldBeNull();
        handle.AllowedHosts.ShouldBeEmpty();
        handle.RestrictToBaseUrlOrigin.ShouldBeFalse();
        handle.FollowRedirects.ShouldBeTrue();
        handle.DefaultTimeoutMilliseconds.ShouldBe(100_000);
        handle.PooledConnectionLifetimeSeconds.ShouldBeNull();
        handle.MaxConnectionsPerServer.ShouldBeNull();
        handle.DefaultHeaders.ShouldBeEmpty();
    }

    [Fact]
    public void ClientNode_RejectsRelativeBaseUrl()
    {
        var registry = HttpResourceTestContext.CreateRegistry();

        var exception = Should.Throw<InvalidOperationException>(
            () => HttpResourceTestContext.CreateResources(
                registry,
                new { baseUrl = "relative/path" }));

        exception.Message.ShouldContain("baseUrl");
    }

    [Fact]
    public void ClientNode_RejectsRestrictToOriginWithoutBaseUrl()
    {
        var registry = HttpResourceTestContext.CreateRegistry();

        var exception = Should.Throw<InvalidOperationException>(
            () => HttpResourceTestContext.CreateResources(
                registry,
                new { restrictToBaseUrlOrigin = true }));

        exception.Message.ShouldContain("restrictToBaseUrlOrigin");
    }

    [Fact]
    public void ClientNode_RejectsEmptyAllowedHostEntry()
    {
        var registry = HttpResourceTestContext.CreateRegistry();

        var exception = Should.Throw<InvalidOperationException>(
            () => HttpResourceTestContext.CreateResources(
                registry,
                new { allowedHosts = new[] { "example.test", "" } }));

        exception.Message.ShouldContain("allowedHosts");
    }

    [Fact]
    public void ClientNode_RejectsInvalidPooledConnectionLifetime()
    {
        var registry = HttpResourceTestContext.CreateRegistry();

        var exception = Should.Throw<InvalidOperationException>(
            () => HttpResourceTestContext.CreateResources(
                registry,
                new { pooledConnectionLifetimeSeconds = 0 }));

        exception.Message.ShouldContain("pooledConnectionLifetimeSeconds");
    }

    [Fact]
    public async Task Request_ReportsNotConnectedForResolvableClient()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 3, 7, 1, 2, TimeSpan.Zero));
        var registry = HttpResourceTestContext.CreateRegistry(options => options.UseClock(clock));
        var resources = HttpResourceTestContext.CreateResources(registry);
        var runtimeNode = CreateRequestNode(
            registry,
            new { client = HttpResourceTestContext.ClientName },
            resources);
        var input = GetInput(runtimeNode);
        var output = LinkOutput<HttpResponseOutput>(runtimeNode, HttpComponentPorts.Output);
        var errors = LinkOutput<HttpErrorOutput>(runtimeNode, HttpComponentPorts.Errors);
        var node = runtimeNode.Node.ShouldBeOfType<Nodes.HttpRequestNode>();
        var diagnostics = new BufferBlock<FluxFlow.Engine.Components.FlowDiagnostic>();
        node.Diagnostics.LinkTo(diagnostics);
        var flowErrors = new BufferBlock<FluxFlow.Engine.Components.FlowError>();
        node.Errors.LinkTo(flowErrors);

        await node.StartAsync();
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/items" });
        input.Target.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // No client => no response is produced; the node reports not connected.
        output.TryReceive(out _).ShouldBeFalse();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Kind.ShouldBe(HttpErrorKind.NotConnected);
        error.Message.ShouldContain("not connected");
        error.Url.ShouldBe("https://example.test/items");
        error.Timestamp.ShouldBe(clock.GetUtcNow());

        var flowError = await flowErrors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        flowError.Code.ShouldBe(HttpErrorCodes.RequestNotConnected);
        flowError.Context.ShouldNotBeNull();
        flowError.Context.ShouldContain($"clientName={HttpResourceTestContext.ClientName}");

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(HttpDiagnosticNames.RequestFailed);
        diagnostic.Level.ShouldBe(FluxFlow.Engine.Components.FlowDiagnosticLevel.Error);
        diagnostic.Attributes["clientName"].ShouldBe(HttpResourceTestContext.ClientName);

        await node.DisposeAsync();
    }

    [Fact]
    public async Task Request_ResolvesRelativeUrlAgainstClientBaseUrlBeforeReportingNotConnected()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        var resources = HttpResourceTestContext.CreateResources(
            registry,
            new { baseUrl = "https://example.test/api/" });
        var runtimeNode = CreateRequestNode(
            registry,
            new { client = HttpResourceTestContext.ClientName },
            resources);
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(runtimeNode, HttpComponentPorts.Errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new HttpRequestInput { Method = "post", Url = "items" });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Kind.ShouldBe(HttpErrorKind.NotConnected);
        error.Method.ShouldBe("POST");
        error.Url.ShouldBe("https://example.test/api/items");
    }

    [Fact]
    public async Task Request_EmitsInvalidUrlErrorAndContinues()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        var resources = HttpResourceTestContext.CreateResources(registry);
        var runtimeNode = CreateRequestNode(
            registry,
            new { client = HttpResourceTestContext.ClientName },
            resources);
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(runtimeNode, HttpComponentPorts.Errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new HttpRequestInput { Url = "relative" });
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/ok" });
        input.Target.Complete();
        var first = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Kind.ShouldBe(HttpErrorKind.InvalidUrl);
        second.Kind.ShouldBe(HttpErrorKind.NotConnected);
    }

    [Fact]
    public async Task Request_UsesClientClockForRequestErrors()
    {
        var startedAt = DateTimeOffset.Parse("2026-06-02T11:00:00Z");
        var elapsed = TimeSpan.FromMilliseconds(1250);
        var failedAt = startedAt + elapsed;
        var clock = new FakeTimeProvider(startedAt) { AutoAdvanceAmount = elapsed };
        var registry = HttpResourceTestContext.CreateRegistry(options => options.UseClock(clock));
        var resources = HttpResourceTestContext.CreateResources(registry);
        var runtimeNode = CreateRequestNode(
            registry,
            new { client = HttpResourceTestContext.ClientName },
            resources);
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(runtimeNode, HttpComponentPorts.Errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new HttpRequestInput { Url = "relative" });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.InvalidUrl);
        error.Timestamp.ShouldBe(failedAt);
        error.ElapsedMilliseconds.ShouldBe(1250);
    }

    [Fact]
    public async Task Request_RejectsHostOutsideClientAllowedHosts()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        var resources = HttpResourceTestContext.CreateResources(
            registry,
            new { allowedHosts = new[] { "api.example.test" } });
        var runtimeNode = CreateRequestNode(
            registry,
            new { client = HttpResourceTestContext.ClientName },
            resources);
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(runtimeNode, HttpComponentPorts.Errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://evil.example.test/items" });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.UrlNotAllowed);
    }

    [Fact]
    public async Task Request_RestrictToBaseUrlOriginBlocksCrossOriginAbsoluteUrl()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        var resources = HttpResourceTestContext.CreateResources(
            registry,
            new
            {
                baseUrl = "https://example.test/api/",
                restrictToBaseUrlOrigin = true
            });
        var runtimeNode = CreateRequestNode(
            registry,
            new { client = HttpResourceTestContext.ClientName },
            resources);
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(runtimeNode, HttpComponentPorts.Errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://attacker.test/steal" });
        await input.Target.SendAsync(new HttpRequestInput { Url = "items" });
        input.Target.Complete();
        var blocked = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var allowed = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        blocked.Kind.ShouldBe(HttpErrorKind.UrlNotAllowed);
        allowed.Kind.ShouldBe(HttpErrorKind.NotConnected);
        allowed.Url.ShouldBe("https://example.test/api/items");
    }

    [Fact]
    public async Task Request_RejectsHeadersWithForbiddenCharacters()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        var resources = HttpResourceTestContext.CreateResources(registry);
        var runtimeNode = CreateRequestNode(
            registry,
            new { client = HttpResourceTestContext.ClientName },
            resources);
        var input = GetInput(runtimeNode);
        var errors = LinkOutput<HttpErrorOutput>(runtimeNode, HttpComponentPorts.Errors);

        await runtimeNode.Node.StartAsync();
        await input.Target.SendAsync(new HttpRequestInput
        {
            Url = "https://example.test/items",
            Headers = new Dictionary<string, string>
            {
                ["X-Injected"] = "value\r\nHost: attacker.test"
            }
        });
        input.Target.Complete();
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Kind.ShouldBe(HttpErrorKind.InvalidRequest);
    }

    [Fact]
    public void Request_RejectsMissingClientOption()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        var resources = HttpResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(HttpComponentTypes.Request, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(HttpResourceTestContext.CreateContext(
                HttpComponentTypes.Request,
                new { maxResponseBodyBytes = 1024 },
                resources)));

        exception.Message.ShouldContain("client");
    }

    [Fact]
    public void Request_FailsWhenClientResourceMissing()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        registry.TryGetFactory(HttpComponentTypes.Request, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(HttpResourceTestContext.CreateContext(
                HttpComponentTypes.Request,
                new { client = "missing-client" },
                new Dictionary<NodeName, RuntimeNode>())));

        exception.Message.ShouldContain("missing-client");
    }

    [Fact]
    public void Request_RejectsInvalidBoundedCapacity()
    {
        var registry = HttpResourceTestContext.CreateRegistry();
        var resources = HttpResourceTestContext.CreateResources(registry);
        registry.TryGetFactory(HttpComponentTypes.Request, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(HttpResourceTestContext.CreateContext(
                HttpComponentTypes.Request,
                new { client = HttpResourceTestContext.ClientName, boundedCapacity = 0 },
                resources)));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void RegisterHttpComponents_RegistersClientAndRequestNodes()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterHttpComponents();

        registry.TryGetFactory(HttpComponentTypes.Client, out _).ShouldBeTrue();
        registry.TryGetFactory(HttpComponentTypes.Request, out _).ShouldBeTrue();
    }

    private static RuntimeNode CreateRequestNode(
        RuntimeNodeFactoryRegistry registry,
        object configuration,
        IReadOnlyDictionary<NodeName, RuntimeNode> resources)
    {
        registry.TryGetFactory(HttpComponentTypes.Request, out var factory).ShouldBeTrue();
        return factory(HttpResourceTestContext.CreateContext(
            HttpComponentTypes.Request,
            configuration,
            resources));
    }

    private static InputPort<HttpRequestInput> GetInput(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(HttpComponentPorts.Input))
            .ShouldBeOfType<InputPort<HttpRequestInput>>();

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }
}
