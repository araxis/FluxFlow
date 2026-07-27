using System.Net;
using System.Text;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Http;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using static FluxFlow.Testing.ComponentDesignMetadataAssertions;
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

    [Fact]
    public void AddHttpComponents_registers_client_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddHttpComponents());

        var client = registry.Components[HttpComponentDefinition.Types.Client];
        client.Inputs[HttpComponentDefinition.Ports.Input].MessageType.ShouldBe(
            typeof(HttpClientRequest));
        client.Outputs[HttpComponentDefinition.Ports.Output].MessageType.ShouldBe(
            typeof(HttpResponseResult));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_http_client_metadata()
    {
        var metadata = GetClientDesignMetadata();

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Type.ShouldBe(new ComponentType(HttpComponentDefinition.Types.Client));
        metadata.DisplayName?.Value.ShouldBe("HTTP Client");
        metadata.Category.ShouldBe(new ComponentCategory("HTTP"));
        metadata.SuggestedEditorWidth.ShouldBe(420);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == HttpComponentDefinition.Resources.Client ||
            option.Name.Value == HttpComponentDefinition.Resources.Clock);
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value)).ShouldBe([
            (HttpComponentDefinition.Resources.Client, 0, true, nameof(HttpClient)),
            (HttpComponentDefinition.Resources.Clock, 1, false, nameof(TimeProvider))
        ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_http_client_ports()
    {
        var metadata = GetClientDesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.ShouldBe(new ComponentPortName(HttpComponentDefinition.Ports.Input));
        input.Direction.ShouldBe(PortDirection.Input);
        input.ValueType?.Value.ShouldBe(nameof(HttpClientRequest));
        input.IsPrimary.ShouldBeTrue();
        input.Order.ShouldBe(0);

        var output = metadata.Ports[1];
        output.Name.ShouldBe(new ComponentPortName(HttpComponentDefinition.Ports.Output));
        output.Direction.ShouldBe(PortDirection.Output);
        output.ValueType?.Value.ShouldBe(nameof(HttpResponseResult));
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
    public void Design_metadata_provider_describes_http_client_option_hints()
    {
        var metadata = GetClientDesignMetadata();
        var options = OptionsByName(metadata);

        AssertOptionHints(
            options["boundedCapacity"],
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            options["maxResponseBodyBytes"],
            "Limits",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            options["treatNonSuccessStatusAsError"],
            "Response",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            options["maxDegreeOfParallelism"],
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            options["defaultTimeoutMilliseconds"],
            "Timeouts",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_http_client_resource_picker_hints()
    {
        var metadata = GetClientDesignMetadata();
        var resources = ResourcesByName(metadata);

        AssertResourceHints(
            resources[HttpComponentDefinition.Resources.Client],
            ResourceDesignMetadataAttributeValues.Client,
            "http-client:{name}");
        AssertResourceHints(
            resources[HttpComponentDefinition.Resources.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddHttpComponents());

        catalog.All.Count.ShouldBe(1);
        catalog.TryGet(
            new ComponentType(HttpComponentDefinition.Types.Client),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("HTTP Client");
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
                var request = FlowMessage.Create(
                    new HttpClientRequest { Method = "GET", Url = "v1/status" },
                    new CorrelationId("http-correlation"));
                var receive = ports.ReceiveAsync<HttpResponseResult>(Output, Timeout);

                (await ports.SendAsync(Input, request)).IsAccepted.ShouldBeTrue();
                var response = (await receive).Message.ShouldNotBeNull();

                response.CorrelationId.ShouldBe(new CorrelationId("http-correlation"));
                var result = response.Value.ShouldBeOfType<HttpResponseResult>();
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
                var receive = ports.ReceiveAsync<HttpResponseResult>(Output, Timeout);
                (await ports.SendAsync(
                    Input,
                    FlowMessage.Create(new HttpClientRequest
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
            registry => registry.AddHttpComponents());

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
                ["primary"]),
            registry => registry.AddHttpComponents(),
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
                ["primary"]),
            registry => registry.AddHttpComponents(),
            registerResources: context =>
                context.Services.AddExternalFluxFlowResource<HttpClient>(
                    ApplicationAddress.Resource("primary"),
                    client));
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ComponentDesignMetadata GetClientDesignMetadata()
        => HttpComponentDefinition.CreateMetadata()
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
