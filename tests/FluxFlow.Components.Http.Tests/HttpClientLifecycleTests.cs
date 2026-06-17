using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

public sealed class HttpClientLifecycleTests
{
    [Fact]
    public async Task ConnectAsync_EstablishesSenderOnceAndExposesBorrow()
    {
        var factory = new RecordingHttpRequestSenderFactory();
        var (handle, _) = CreateClient(factory);

        handle.State.ShouldBe(HttpClientConnectionState.Disconnected);
        handle.TryGetSender(out _).ShouldBeFalse();

        await handle.ConnectAsync();

        handle.State.ShouldBe(HttpClientConnectionState.Connected);
        factory.CreateClientCalls.ShouldBe(1);
        handle.TryGetSender(out var borrowed).ShouldBeTrue();
        borrowed.ShouldBeSameAs(factory.LastSender);

        // Idempotent: a second connect does not build a second sender.
        await handle.ConnectAsync();
        factory.CreateClientCalls.ShouldBe(1);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task DisconnectAsync_DisposesSenderOnceAndStopsBorrows()
    {
        var factory = new RecordingHttpRequestSenderFactory();
        var (handle, _) = CreateClient(factory);

        await handle.ConnectAsync();
        handle.TryGetSender(out _).ShouldBeTrue();
        var sender = factory.LastSender;

        await handle.DisconnectAsync();

        handle.State.ShouldBe(HttpClientConnectionState.Disconnected);
        handle.TryGetSender(out _).ShouldBeFalse();
        sender.DisposeCalls.ShouldBe(1);

        // Idempotent disconnect does not double-dispose.
        await handle.DisconnectAsync();
        sender.DisposeCalls.ShouldBe(1);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_IsSingleFlight_TwoConcurrentConnectsBuildOneSender()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new RecordingHttpRequestSenderFactory(buildGate: gate.Task);
        var (handle, _) = CreateClient(factory);

        var first = handle.ConnectAsync();
        var second = handle.ConnectAsync();

        // Both calls observe the same in-flight build; release the factory.
        gate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        factory.CreateClientCalls.ShouldBe(1);
        handle.State.ShouldBe(HttpClientConnectionState.Connected);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_TearsDownSenderIdempotently()
    {
        var factory = new RecordingHttpRequestSenderFactory();
        var (handle, _) = CreateClient(factory);

        await handle.ConnectAsync();
        var sender = factory.LastSender;

        await ((IAsyncDisposable)handle).DisposeAsync();
        sender.DisposeCalls.ShouldBe(1);
        handle.State.ShouldBe(HttpClientConnectionState.Disconnected);

        // Idempotent dispose.
        await ((IAsyncDisposable)handle).DisposeAsync();
        sender.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Request_RoundTripsThroughSenderOnlyWhileConnected()
    {
        HttpResponseOutput Respond(HttpRequestSendContext context) => new()
        {
            Method = context.Method,
            Url = context.Url.ToString(),
            StatusCode = 200,
            ReasonPhrase = "OK",
            Body = "pong",
            ContentType = "text/plain",
            Success = true
        };

        var factory = new RecordingHttpRequestSenderFactory(Respond);
        var (handle, resources) = CreateClient(factory);
        var request = CreateRequestNode(resources);
        var input = GetInput(request);
        var output = LinkOutput<HttpResponseOutput>(request, HttpComponentPorts.Output);
        var errors = LinkOutput<HttpErrorOutput>(request, HttpComponentPorts.Errors);
        await request.Node.StartAsync();

        // Before connect: the borrow fails -> RequestNotConnected.
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/ping" });
        var notConnected = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        notConnected.Kind.ShouldBe(HttpErrorKind.NotConnected);
        output.TryReceive(out _).ShouldBeFalse();

        // After ConnectAsync: the request round-trips through the in-memory sender.
        await handle.ConnectAsync();
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/ping" });
        var response = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        response.StatusCode.ShouldBe(200);
        response.Body.ShouldBe("pong");
        response.Url.ShouldBe("https://example.test/ping");
        factory.LastSender.Sent.Count.ShouldBe(1);

        // After DisconnectAsync: borrows fail again and the sender was disposed once.
        var sender = factory.LastSender;
        await handle.DisconnectAsync();
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://example.test/ping" });
        var notConnectedAgain = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        notConnectedAgain.Kind.ShouldBe(HttpErrorKind.NotConnected);
        sender.DisposeCalls.ShouldBe(1);

        input.Target.Complete();
        await request.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await ((IAsyncDisposable)request.Node).DisposeAsync();
        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task ConnectedSender_HasAutoRedirectDisabled_WhenAllowListConfigured()
    {
        // Use the real production factory so the redirect-guard decision under
        // test is the shipped one, not a test stand-in.
        var factory = new HttpClientRequestSenderFactory();
        var registry = HttpResourceTestContext.CreateRegistry(
            options => options.UseRequestSenderFactory(factory));
        var resources = HttpResourceTestContext.CreateResources(
            registry,
            new
            {
                baseUrl = "https://example.test/api/",
                allowedHosts = new[] { "example.test" },
                restrictToBaseUrlOrigin = true,
                followRedirects = true
            });
        var handle = HttpResourceTestContext.ResolveHandle(resources);

        await handle.ConnectAsync();

        handle.TryGetSender(out var sender).ShouldBeTrue();
        var redirectPolicy = sender.ShouldBeAssignableTo<IHttpRedirectPolicy>()!;
        redirectPolicy.AllowAutoRedirect.ShouldBeFalse();

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task ConnectedSender_FollowsRedirects_WhenNoGuardConfigured()
    {
        var factory = new HttpClientRequestSenderFactory();
        var registry = HttpResourceTestContext.CreateRegistry(
            options => options.UseRequestSenderFactory(factory));
        var resources = HttpResourceTestContext.CreateResources(
            registry,
            new { followRedirects = true });
        var handle = HttpResourceTestContext.ResolveHandle(resources);

        await handle.ConnectAsync();

        handle.TryGetSender(out var sender).ShouldBeTrue();
        sender.ShouldBeAssignableTo<IHttpRedirectPolicy>()!.AllowAutoRedirect.ShouldBeTrue();

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task Request_EnsureUrlAllowedStillRejectsDisallowedHostAfterConnect()
    {
        var factory = new RecordingHttpRequestSenderFactory();
        var registry = HttpResourceTestContext.CreateRegistry(
            options => options.UseRequestSenderFactory(factory));
        var resources = HttpResourceTestContext.CreateResources(
            registry,
            new { allowedHosts = new[] { "api.example.test" } });
        var handle = HttpResourceTestContext.ResolveHandle(resources);
        var request = CreateRequestNode(resources, registry);
        var input = GetInput(request);
        var output = LinkOutput<HttpResponseOutput>(request, HttpComponentPorts.Output);
        var errors = LinkOutput<HttpErrorOutput>(request, HttpComponentPorts.Errors);
        await request.Node.StartAsync();

        await handle.ConnectAsync();

        // Even with a live sender, the per-request allow-list guard runs first and
        // rejects the disallowed host BEFORE the sender is borrowed.
        await input.Target.SendAsync(new HttpRequestInput { Url = "https://evil.example.test/items" });
        var blocked = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        blocked.Kind.ShouldBe(HttpErrorKind.UrlNotAllowed);
        output.TryReceive(out _).ShouldBeFalse();
        factory.LastSender.Sent.ShouldBeEmpty();

        input.Target.Complete();
        await request.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await ((IAsyncDisposable)request.Node).DisposeAsync();
        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    private static (IHttpClientHandle Handle, IReadOnlyDictionary<NodeName, RuntimeNode> Resources)
        CreateClient(
            RecordingHttpRequestSenderFactory factory,
            object? configuration = null)
    {
        var registry = HttpResourceTestContext.CreateRegistry(
            options => options.UseRequestSenderFactory(factory));
        var resources = HttpResourceTestContext.CreateResources(registry, configuration);
        var handle = HttpResourceTestContext.ResolveHandle(resources);
        return (handle, resources);
    }

    private static RuntimeNode CreateRequestNode(
        IReadOnlyDictionary<NodeName, RuntimeNode> resources,
        RuntimeNodeFactoryRegistry? registry = null)
    {
        registry ??= HttpResourceTestContext.CreateRegistry();
        registry.TryGetFactory(HttpComponentTypes.Request, out var factory).ShouldBeTrue();
        return factory(HttpResourceTestContext.CreateContext(
            HttpComponentTypes.Request,
            new { client = HttpResourceTestContext.ClientName },
            resources));
    }

    private static InputPort<HttpRequestInput> GetInput(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(HttpComponentPorts.Input))
            .ShouldBeOfType<InputPort<HttpRequestInput>>();

    private static BufferBlock<T> LinkOutput<T>(RuntimeNode runtimeNode, string portName)
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
