using System.Net;
using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.Composition.Tests;

public sealed class HttpCompositionNodeRegistryExtensionsTests
{
    [Fact]
    public void RegisterHttpNodes_registers_client_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterHttpNodes();

        var client = registry.Registrations[HttpCompositionNodeTypes.Client];
        client.Inputs[HttpCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(HttpRequestInput));
        client.Outputs[HttpCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(HttpResponseOutput));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_http_client_metadata()
    {
        var metadata = GetClientDesignMetadata();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType(HttpCompositionNodeTypes.Client));
        metadata.DisplayName.ShouldBe("HTTP Client");
        metadata.Category.ShouldBe("HTTP");
        metadata.SuggestedEditorWidth.ShouldBe(420);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == HttpCompositionResourceNames.Client ||
            option.Name.Value == HttpCompositionResourceNames.Clock);
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType)).ShouldBe([
            (HttpCompositionResourceNames.Client, 0, true, nameof(HttpClient)),
            (HttpCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
        ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_http_client_ports()
    {
        var metadata = GetClientDesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.ShouldBe(new ComponentPortName(HttpCompositionPortNames.Input));
        input.Direction.ShouldBe(PortDirection.Input);
        input.ValueType.ShouldBe(nameof(HttpRequestInput));
        input.IsPrimary.ShouldBeTrue();
        input.Order.ShouldBe(0);

        var output = metadata.Ports[1];
        output.Name.ShouldBe(new ComponentPortName(HttpCompositionPortNames.Output));
        output.Direction.ShouldBe(PortDirection.Output);
        output.ValueType.ShouldBe(nameof(HttpResponseOutput));
        output.IsPrimary.ShouldBeTrue();
        output.Order.ShouldBe(1);
    }

    [Fact]
    public void Design_metadata_provider_describes_http_client_options()
    {
        var metadata = GetClientDesignMetadata();
        var defaults = HttpClientNodeOptions.Default;

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "boundedCapacity",
            "maxResponseBodyBytes",
            "treatNonSuccessStatusAsError",
            "maxDegreeOfParallelism",
            "defaultTimeoutMilliseconds"
        ], ignoreOrder: false);

        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);
        AssertOption(
            metadata,
            "maxResponseBodyBytes",
            OptionValueKind.Number,
            defaults.MaxResponseBodyBytes,
            min: 1);
        AssertOption(
            metadata,
            "treatNonSuccessStatusAsError",
            OptionValueKind.Boolean,
            defaults.TreatNonSuccessStatusAsError);
        AssertOption(
            metadata,
            "maxDegreeOfParallelism",
            OptionValueKind.Number,
            defaults.MaxDegreeOfParallelism,
            min: 1);
        AssertOption(
            metadata,
            "defaultTimeoutMilliseconds",
            OptionValueKind.Number,
            defaultValue: null,
            min: 1);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new HttpComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.Count.ShouldBe(1);
        catalog.TryGet(
            new ComponentType(HttpCompositionNodeTypes.Client),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().DisplayName.ShouldBe("HTTP Client");
    }

    [Fact]
    public async Task Hosted_client_node_resolves_keyed_http_client_and_sends_request()
    {
        var handler = new RecordingHandler(
            (_, _) => Respond(HttpStatusCode.OK, "pong", "text/plain"));
        var services = new ServiceCollection();
        services.AddKeyedSingleton(
            "primary",
            (_, _) => new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.example.test/")
            });
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "api",
                    HttpCompositionNodeTypes.Client,
                    node => node
                        .Resource(HttpCompositionResourceNames.Client, "primary")
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterHttpNodes())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var runtime = host.Runtime.ShouldNotBeNull();
        var clientNode = runtime.Nodes.ShouldHaveSingleItem();
        var input = clientNode.Descriptor.Inputs[HttpCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<HttpRequestInput>>();
        var output = clientNode.Descriptor.Outputs[HttpCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<HttpResponseOutput>>();
        var responses = new BufferBlock<FlowMessage<HttpResponseOutput>>();
        output.Source.LinkTo(
            responses,
            new DataflowLinkOptions { PropagateCompletion = true });

        var request = FlowMessage.Create(
            new HttpRequestInput { Method = "GET", Url = "v1/status" },
            new CorrelationId("http-correlation"));

        (await input.Target.SendAsync(request)
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
        input.Target.Complete();

        var response = await responses.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        response.CorrelationId.ShouldBe(new CorrelationId("http-correlation"));
        response.Payload.StatusCode.ShouldBe(200);
        response.Payload.Body.ShouldBe("pong");
        handler.LastRequest!.RequestUri!.ToString()
            .ShouldBe("https://api.example.test/v1/status");
    }

    [Fact]
    public async Task Hosted_client_node_binds_options_from_configuration()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(
            "primary",
            (_, _) => new HttpClient(new RecordingHandler(
                (_, _) => Respond(HttpStatusCode.InternalServerError, "boom", "text/plain"))));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "api",
                    HttpCompositionNodeTypes.Client,
                    node => node
                        .Resource(HttpCompositionResourceNames.Client, "primary")
                        .Configure("treatNonSuccessStatusAsError", true)
                        .Configure("boundedCapacity", 8)))
                .Build())
            .RegisterNodes(registry => registry.RegisterHttpNodes())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        var runtime = host.Runtime.ShouldNotBeNull();
        var clientNode = runtime.Nodes.ShouldHaveSingleItem();
        var input = clientNode.Descriptor.Inputs[HttpCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<HttpRequestInput>>();
        var output = clientNode.Descriptor.Outputs[HttpCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<HttpResponseOutput>>();
        var errors = clientNode.Descriptor.Errors.ShouldNotBeNull();
        var errorSink = new BufferBlock<FlowError>();
        errors.LinkTo(errorSink);
        var responseSink = new BufferBlock<FlowMessage<HttpResponseOutput>>();
        output.Source.LinkTo(responseSink);

        (await input.Target.SendAsync(FlowMessage.Create(
                new HttpRequestInput { Url = "https://example.test/" }))
            .WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();

        var error = await errorSink.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        responseSink.Count.ShouldBe(0);
        error.Context.ShouldNotBeNull().ShouldContain("statusCode=500");
    }

    [Fact]
    public async Task Missing_client_resource_reference_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "api",
                    HttpCompositionNodeTypes.Client))
                .Build())
            .RegisterNodes(registry => registry.RegisterHttpNodes())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                HttpCompositionResourceNames.Client,
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("boundedCapacity", 0, "BoundedCapacity")]
    [InlineData("maxResponseBodyBytes", 0, "MaxResponseBodyBytes")]
    [InlineData("maxDegreeOfParallelism", 0, "MaxDegreeOfParallelism")]
    [InlineData("defaultTimeoutMilliseconds", 0, "DefaultTimeoutMilliseconds")]
    public async Task Invalid_client_options_surface_factory_diagnostic(
        string optionName,
        int optionValue,
        string expectedMessage)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton(
            "primary",
            (_, _) => new HttpClient(new RecordingHandler(
                (_, _) => Respond(HttpStatusCode.OK, "ok", "text/plain"))));
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "api",
                    HttpCompositionNodeTypes.Client,
                    node => node
                        .Resource(HttpCompositionResourceNames.Client, "primary")
                        .Configure(optionName, optionValue)))
                .Build())
            .RegisterNodes(registry => registry.RegisterHttpNodes())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await hostedService.StartAsync(CancellationToken.None);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    private static ComponentDesignMetadata GetClientDesignMetadata()
        => new HttpComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static Task<HttpResponseMessage> Respond(
        HttpStatusCode status,
        string body,
        string? contentType)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
        };
        if (contentType is not null)
        {
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        return Task.FromResult(response);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = await handler(request, cancellationToken)
                .ConfigureAwait(false);
            response.RequestMessage ??= request;
            return response;
        }
    }
}
