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
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Http.Composition.Tests;

public sealed class HttpCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", HttpCompositionPortNames.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", HttpCompositionPortNames.Output);

    [Fact]
    public void RegisterHttpNodes_registers_client_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterHttpNodes();

        var client = registry.Registrations[HttpCompositionNodeTypes.Client];
        client.Inputs[HttpCompositionPortNames.Input].MessageType.ShouldBe(
            typeof(HttpClientRequest));
        client.Outputs[HttpCompositionPortNames.Output].MessageType.ShouldBe(
            typeof(HttpClientResult));
    }

    [Fact]
    public void Typed_registration_preserves_released_request_and_response_contracts()
    {
        const string nodeType = "http.client.response-output";
        var registry = new CompositionNodeRegistry()
            .RegisterHttpResponseOutput(nodeType);

        var client = registry.Registrations[nodeType];
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
        metadata.DisplayName?.Value.ShouldBe("HTTP Client");
        metadata.Category.ShouldBe(new ComponentCategory("HTTP"));
        metadata.SuggestedEditorWidth.ShouldBe(420);
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == HttpCompositionResourceNames.Client ||
            option.Name.Value == HttpCompositionResourceNames.Clock);
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value)).ShouldBe([
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
        input.ValueType?.Value.ShouldBe(nameof(HttpClientRequest));
        input.IsPrimary.ShouldBeTrue();
        input.Order.ShouldBe(0);

        var output = metadata.Ports[1];
        output.Name.ShouldBe(new ComponentPortName(HttpCompositionPortNames.Output));
        output.Direction.ShouldBe(PortDirection.Output);
        output.ValueType?.Value.ShouldBe(nameof(HttpClientResult));
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
            resources[HttpCompositionResourceNames.Client],
            ResourceDesignMetadataAttributeValues.Client,
            "http-client:{name}");
        AssertResourceHints(
            resources[HttpCompositionResourceNames.Clock],
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
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
                var receive = ports.ReceiveAsync<HttpClientResult>(Output, Timeout);

                (await ports.SendAsync(Input, request)).IsAccepted.ShouldBeTrue();
                var response = (await receive).Message.ShouldNotBeNull();

                response.CorrelationId.ShouldBe(new CorrelationId("http-correlation"));
                var result = response.Payload.ShouldBeOfType<HttpResponseResult>();
                result.StatusCode.ShouldBe(200);
                Encoding.UTF8.GetString(result.Body.OriginalBytes.AsSpan()).ShouldBe("pong");
                handler.LastRequest!.RequestUri!.ToString()
                    .ShouldBe("https://api.example.test/v1/status");

                await host.RevisionHost.StopApplicationAsync();
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
                var receive = ports.ReceiveAsync<HttpClientResult>(Output, Timeout);
                (await ports.SendAsync(
                    Input,
                    FlowMessage.Create(new HttpClientRequest
                    {
                        Url = "https://example.test/"
                    }))).IsAccepted.ShouldBeTrue();

                var result = (await receive).Message.ShouldNotBeNull()
                    .Payload.ShouldBeOfType<HttpClientFailureResult>();
                result.Error!.Code.ShouldBe(HttpErrorCodeNames.NonSuccessStatus);
                result.Response.ShouldNotBeNull().StatusCode.ShouldBe(500);
            },
            Properties(
                ("treatNonSuccessStatusAsError", true),
                ("boundedCapacity", 8)));
    }

    [Fact]
    public async Task Missing_client_resource_reference_surfaces_factory_diagnostic()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(HttpCompositionNodeTypes.Client),
            registry => registry.RegisterHttpNodes());

        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details.GetObject()["exceptionMessage"].GetString().Contains(
                HttpCompositionResourceNames.Client,
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
        componentProperties[HttpCompositionResourceNames.Client] = "Resources.primary";

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                HttpCompositionNodeTypes.Client,
                componentProperties,
                ["primary"]),
            registry => registry.RegisterHttpNodes(),
            configureRuntimeServices: context =>
                context.Services.AddExternalFluxFlowResource<HttpClient>(
                    ApplicationAddress.Resource("primary"),
                    client));

        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.StartResult.Update.Failures.ShouldContain(failure =>
            failure.Stage == ApplicationRevisionFailureStage.Preparation &&
            failure.Error.Details.GetObject()["exceptionMessage"].GetString().Contains(
                expectedMessage,
                StringComparison.Ordinal));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static async Task WithClientNodeAsync(
        HttpClient client,
        Func<ApplicationPortRuntime, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        var componentProperties = (properties ?? new Dictionary<string, object?>())
            .ToDictionary(
                static property => property.Key,
                static property => property.Value,
                StringComparer.Ordinal);
        componentProperties[HttpCompositionResourceNames.Client] = "Resources.primary";

        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                HttpCompositionNodeTypes.Client,
                componentProperties,
                ["primary"]),
            registry => registry.RegisterHttpNodes(),
            configureRuntimeServices: context =>
                context.Services.AddExternalFluxFlowResource<HttpClient>(
                    ApplicationAddress.Resource("primary"),
                    client));
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ComponentDesignMetadata GetClientDesignMetadata()
        => new HttpComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

    private static Dictionary<string, ResourceDesignMetadata> ResourcesByName(
        ComponentDesignMetadata metadata)
        => metadata.Resources.ToDictionary(
            resource => resource.Name.Value,
            StringComparer.Ordinal);

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

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);

        if (editor is null)
        {
            option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
                .ShouldBe(editor);
        }

        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
            .ShouldBeFalse();
        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
            .ShouldBeFalse();
    }

    private static void AssertResourceHints(
        ResourceDesignMetadata resource,
        string pickerKind,
        string keyPattern)
    {
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(pickerKind);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe(keyPattern);
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

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
