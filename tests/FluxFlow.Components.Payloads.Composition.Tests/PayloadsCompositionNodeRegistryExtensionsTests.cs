using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Payloads;
using FluxFlow.Components.Payloads.Composition;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Payloads.Composition.Tests;

public sealed class PayloadsCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void RegisterPayloadInspect_registers_request_result_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterPayloadInspect();

        var registration = registry.Registrations[PayloadsCompositionNodeTypes.Inspect];
        registration.Inputs[PayloadsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(PayloadInspectionRequest));
        registration.Outputs[PayloadsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(PayloadInspectionResult));
    }

    [Fact]
    public async Task Hosted_payload_inspect_classifies_json_and_preserves_correlation_id()
    {
        var result = await RunNodeAsync(
            new PayloadInspectionRequest
            {
                Text = """{"name":"flux","count":2}""",
                ContentType = "application/json"
            },
            node => node.Configure("maxPreviewBytes", 128));

        result.CorrelationId.ShouldBe(new CorrelationId("payload.inspect"));
        result.Payload.Kind.ShouldBe(PayloadKind.JsonObject);
        result.Payload.ContentType.ShouldBe("application/json");
        result.Payload.TextPreview.ShouldNotBeNull().ShouldContain("\"name\"");
        result.Payload.FormattedPreview.ShouldNotBeNull().ShouldContain("\n");
    }

    [Fact]
    public async Task Hosted_payload_inspect_binds_options_from_configuration()
    {
        var result = await RunNodeAsync(
            new PayloadInspectionRequest
            {
                Text = """{"message":"abcdef"}""",
                ContentType = "application/json"
            },
            node => node
                .Configure("maxPreviewBytes", 3)
                .Configure("maxFormattedChars", 10));

        result.Payload.TextPreview.ShouldBe("""{"m""");
        result.Payload.TextPreviewTruncated.ShouldBeTrue();
        result.Payload.FormattedPreview.ShouldNotBeNull();
        result.Payload.FormattedPreview!.Length.ShouldBe(10);
        result.Payload.FormattedPreviewTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_payload_inspect_uses_optional_keyed_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        var result = await RunNodeAsync(
            new PayloadInspectionRequest { Text = "hello" },
            node => node.Resource(PayloadsCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock));

        result.Payload.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Hosted_payload_inspect_emits_errors_and_continues()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            var results = Link(output.Source);
            var errors = Link(descriptor.Errors.ShouldNotBeNull());
            var bad = FlowMessage.Create(
                new PayloadInspectionRequest
                {
                    Text = "hello",
                    EncodingHint = "missing-encoding"
                },
                new CorrelationId("bad"));
            var good = FlowMessage.Create(
                new PayloadInspectionRequest { Text = "hello" },
                new CorrelationId("good"));

            (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
            (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

            var error = await errors.ReceiveAsync().WaitAsync(Timeout);
            var result = await results.ReceiveAsync().WaitAsync(Timeout);

            error.Code.ShouldBe(PayloadErrorCodes.UnsupportedEncoding);
            error.CorrelationId.ShouldBe(bad.CorrelationId);
            result.CorrelationId.ShouldBe(good.CorrelationId);
            result.Payload.Kind.ShouldBe(PayloadKind.Text);
            result.Payload.TextPreview.ShouldBe("hello");
        });
    }

    [Fact]
    public async Task Hosted_payload_inspect_exposes_events()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            output.Source.LinkTo(DataflowBlock.NullTarget<FlowMessage<PayloadInspectionResult>>());
            var events = Link(descriptor.Events.ShouldNotBeNull());
            var message = FlowMessage.Create(new PayloadInspectionRequest { Text = "hello" });

            (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

            var @event = await events.ReceiveAsync().WaitAsync(Timeout);
            @event.Name.ShouldBe(PayloadDiagnosticNames.Inspected);
            @event.CorrelationId.ShouldBe(message.CorrelationId);
        });
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "inspect",
                    PayloadsCompositionNodeTypes.Inspect,
                    node => node.Configure("boundedCapacity", 0)))
                .Build())
            .RegisterNodes(registry => registry.RegisterPayloadInspect())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                "boundedCapacity",
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<FlowMessage<PayloadInspectionResult>> RunNodeAsync(
        PayloadInspectionRequest request,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        FlowMessage<PayloadInspectionResult>? result = null;
        await WithNodeAsync(
            async (input, output, _) =>
            {
                var results = Link(output.Source);
                var message = FlowMessage.Create(
                    request,
                    new CorrelationId(PayloadsCompositionNodeTypes.Inspect));

                (await input.Target.SendAsync(message).WaitAsync(Timeout))
                    .ShouldBeTrue();

                result = await results.ReceiveAsync().WaitAsync(Timeout);
            },
            configureNode,
            configureServices);

        return result.ShouldNotBeNull();
    }

    private static async Task WithNodeAsync(
        Func<
            CompositionInputPort<PayloadInspectionRequest>,
            CompositionOutputPort<PayloadInspectionResult>,
            ComposedNode,
            Task> run,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "node",
                    PayloadsCompositionNodeTypes.Inspect,
                    configureNode))
                .Build())
            .RegisterNodes(registry => registry.RegisterPayloadInspect())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;
        var input = descriptor.Inputs[PayloadsCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<PayloadInspectionRequest>>();
        var output = descriptor.Outputs[PayloadsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<PayloadInspectionResult>>();

        await run(input, output, descriptor);
    }

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }
}
