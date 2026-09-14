using System.Threading.Tasks.Dataflow;
using System.Text.Json;
using FluxFlow.Json.Model;
using System.Net;
using System.Text;
using FluxFlow.Components.Http;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Http.Composition.Tests;

public sealed class HttpServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", HttpComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", HttpComponentDefinition.Ports.Output);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_factory_preserves_parallel_profile_capability(bool preserveOrder)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new HttpClient(new RecordingHandler(async (_, cancellation) =>
        {
            if (Interlocked.Increment(ref calls) == 2) entered.TrySetResult();
            await release.Task.WaitAsync(cancellation);
            return await Respond(HttpStatusCode.OK, "ok", "text/plain");
        }));
        var services = new ServiceCollection().AddHttp();
        services.AddExternalFluxFlowResource<HttpClient>(ApplicationAddress.Resource("client"), client);
        services.AddExternalFluxFlowResource<CompositionProcessingProfile>(ApplicationAddress.Resource("parallel"),
            new CompositionProcessingProfile
            {
                Mode = CompositionProcessingMode.Parallel,
                Order = preserveOrder ? CompositionProcessingOrder.Preserve : CompositionProcessingOrder.Relaxed
            });
        await using var provider = services.BuildServiceProvider();
        var definition = new ComponentDefinition(HttpComponentDefinition.Types.Client, new[] {
            new KeyValuePair<string, JsonElement>("client", JsonSerializer.SerializeToElement("Resources.client")),
            new KeyValuePair<string, JsonElement>("Processing", JsonSerializer.SerializeToElement("Resources.parallel"))
        });
        var activation = FluxFlowRegistrationExtensions.CreateFactoryMap(provider)[definition.Type](
            new ComponentActivationContext(provider, "main", "node", definition)).AsTask();
        if (preserveOrder)
        {
            await Should.ThrowAsync<InvalidOperationException>(() => activation);
            return;
        }
        var instance = await activation;
        using var link = instance.Outputs["Output"].Source.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
        try
        {
            var request = HttpRequest(new HttpClientRequest { Method = "GET", Url = "https://example.test/" }, CorrelationId.New());
            await instance.Inputs["Input"].Target.SendAsync(request);
            await instance.Inputs["Input"].Target.SendAsync(request);
            await entered.Task.WaitAsync(Timeout);
        }
        finally
        {
            release.TrySetResult();
            await instance.DisposeAsync();
        }
    }

    [Fact]
    public async Task AddHttp_registers_client_ports()
    {
        var services = new ServiceCollection().AddHttp();
        using var client = new HttpClient();
        services.AddExternalFluxFlowResource<HttpClient>(ApplicationAddress.Resource("client"), client);
        await using var provider = services.BuildServiceProvider();
        var type = HttpComponentDefinition.Types.Client;
        var instance = await FluxFlowRegistrationExtensions.CreateFactoryMap(provider)[type](
            new ComponentActivationContext(provider, "main", "node", new ComponentDefinition(type, new[] { new KeyValuePair<string, JsonElement>("client", JsonSerializer.SerializeToElement("Resources.client")) })));
        try
        {
            instance.Inputs.Keys.ShouldBe(new[] { "Input" });
            instance.Outputs.Keys.ShouldBe(new[] { "Output", "Events" });
            instance.InputMetadata.Values.ShouldAllBe(p => p.MessageType == typeof(FlowValue) && p.Kind == ComponentPortKind.Message);
            instance.OutputMetadata.Values.ShouldAllBe(p => p.MessageType == typeof(FlowValue));
        }
        finally { await instance.DisposeAsync(); }
    }

    [Fact]
    public async Task Hosted_client_node_resolves_keyed_http_client_and_sends_request()
    {
        var handler = new RecordingHandler(
            (_, _) => Respond(HttpStatusCode.OK, "pong", "text/plain"));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/")
        };

        await WithClientNodeAsync(
            client,
            async (ports, host) =>
            {
                var request = HttpRequest(
                    new HttpClientRequest { Method = "GET", Url = "v1/status" },
                    new CorrelationId("http-correlation"));
                var receive = ports.ReceiveAsync<FlowValue>(Output, Timeout);

                (await ports.SendAsync(Input, request)).IsAccepted.ShouldBeTrue();
                var response = (await receive).Message.ShouldNotBeNull();

                response.CorrelationId.ShouldBe(new CorrelationId("http-correlation"));
                var result = response.Value.Json.Deserialize<HttpResponseResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                result.StatusCode.ShouldBe(200);
                Encoding.UTF8.GetString(result.Body.Bytes.AsSpan()).ShouldBe("pong");
                handler.LastRequest!.RequestUri!.ToString()
                    .ShouldBe("https://api.example.test/v1/status");

                await host.RevisionHost.StopAsync();
            },
            Properties(("boundedCapacity", 8)));
    }

    [Fact]
    public async Task Hosted_client_node_binds_options_from_configuration()
    {
        using var client = new HttpClient(new RecordingHandler(
            (_, _) => Respond(HttpStatusCode.InternalServerError, "boom", "text/plain")));

        await WithClientNodeAsync(
            client,
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<FlowValue>(Output, Timeout);
                (await ports.SendAsync(
                    Input,
                    HttpRequest(new HttpClientRequest
                    {
                        Url = "https://example.test/"
                    }))).IsAccepted.ShouldBeTrue();

                var result = (await receive).Message.ShouldNotBeNull();
                result.IsError.ShouldBeTrue();
                result.Error!.Code.ShouldBe(HttpErrorCodeNames.NonSuccessStatus);
                result.Error.Details!.Value.GetProperty("statusCode").GetInt32().ShouldBe(500);
            },
            Properties(
                ("treatNonSuccessStatusAsError", true),
                ("boundedCapacity", 8)));
    }

    [Fact]
    public async Task Missing_client_resource_reference_surfaces_factory_diagnostic()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(HttpComponentDefinition.Types.Client),
            registry => registry.AddHttp());

        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                HttpComponentDefinition.Resources.Client,
                StringComparison.Ordinal));
        host.RuntimeAccess.Ports.ShouldBeNull();
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
        using var client = new HttpClient(new RecordingHandler(
            (_, _) => Respond(HttpStatusCode.OK, "ok", "text/plain")));
        var properties = Properties((optionName, optionValue));
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        componentProperties[HttpComponentDefinition.Resources.Client] = "Resources.primary";

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                HttpComponentDefinition.Types.Client,
                componentProperties,
                ["primary"], discardUnmatched: ["Output", "Events"]),
            registry => registry.AddHttp(),
            registerResources: context =>
                context.Services.AddExternalFluxFlowResource<HttpClient>(
                    ApplicationAddress.Resource("primary"),
                    client));

        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                expectedMessage,
                StringComparison.Ordinal));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static FlowMessage HttpRequest(
        HttpClientRequest request,
        CorrelationId? correlationId = null)
        => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId);

    private static async Task WithClientNodeAsync(
        HttpClient client,
        Func<ApplicationPorts, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        var componentProperties = (properties ?? new Dictionary<string, object?>())
            .ToDictionary(
                static property => property.Key,
                static property => property.Value,
                StringComparer.Ordinal);
        componentProperties[HttpComponentDefinition.Resources.Client] = "Resources.primary";

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                HttpComponentDefinition.Types.Client,
                componentProperties,
                ["primary"], discardUnmatched: ["Output", "Events"]),
            registry => registry.AddHttp(),
            registerResources: context =>
                context.Services.AddExternalFluxFlowResource<HttpClient>(
                    ApplicationAddress.Resource("primary"),
                    client));
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
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
