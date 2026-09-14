using FluxFlow.Composition.Definition;
using System.Text;
using System.Text.Json;
using FluxFlow.Components.Serialization.Diagnostics;
using FluxFlow.Components.Serialization.Options;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Serialization.Composition.Tests;

public sealed class SerializationServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        SerializationComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        SerializationComponentDefinition.Ports.Output);
    private static readonly ApplicationAddress Events = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        "Events");

    [Fact]
    public void AddSerialization_registers_six_lazy_factories_idempotently()
    {
        using var provider = new ServiceCollection().AddSerialization().AddSerialization().BuildServiceProvider();
        FluxFlowRegistrationExtensions.CreateFactoryMap(provider).Keys.OrderBy(x => x).ShouldBe(new[] {
            "base64.decode", "base64.encode", "json.parse", "json.stringify", "text.decode", "text.encode"
        });
    }

    [Fact]
    public async Task Hosted_json_parse_binds_options_and_returns_json()
    {
        var result = await RunNodeAsync<FlowContent, JsonElement>(
            SerializationComponentDefinition.Types.JsonParse,
            FlowContent.FromBytes(
                Encoding.UTF8.GetBytes("""{"name":"sample",}"""),
                "application/json"),
            Properties(("allowTrailingCommas", true)));

        result.CorrelationId.ShouldBe(new CorrelationId("json.parse"));
        result.IsError.ShouldBeFalse();
        result.Value.GetProperty("name").GetString().ShouldBe("sample");
    }

    [Fact]
    public async Task Hosted_json_stringify_returns_json_content()
    {
        var result = await RunNodeAsync<JsonElement, FlowContent>(
            SerializationComponentDefinition.Types.JsonStringify,
            JsonSerializer.SerializeToElement(new { ok = true }));

        var content = result.Value;
        content.ContentType.ShouldBe("application/json");
        Encoding.UTF8.GetString(content.Bytes.AsSpan())
            .ShouldBe("""{"ok":true}""");
    }

    [Fact]
    public async Task Hosted_text_encode_returns_text_content()
    {
        var result = await RunNodeAsync<string, FlowContent>(
            SerializationComponentDefinition.Types.TextEncode,
            "hello");

        var content = result.Value;
        content.ContentType.ShouldBe("text/plain");
        content.Encoding.ShouldBe("utf-8");
        Encoding.UTF8.GetString(content.Bytes.AsSpan()).ShouldBe("hello");
    }

    [Fact]
    public async Task Hosted_text_decode_binds_encoding_and_skips_preamble()
    {
        var encoding = Encoding.Unicode;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("hello")).ToArray();
        var result = await RunNodeAsync<FlowContent, string>(
            SerializationComponentDefinition.Types.TextDecode,
            FlowContent.FromBytes(bytes, "text/plain"),
            Properties(("defaultEncoding", "utf-16")));

        result.Value.ShouldBe("hello");
    }

    [Fact]
    public async Task Hosted_base64_encode_returns_string_value()
    {
        var result = await RunNodeAsync<FlowContent, string>(
            SerializationComponentDefinition.Types.Base64Encode,
            FlowContent.FromBytes(Encoding.UTF8.GetBytes("hello")));

        result.Value.ShouldBe("aGVsbG8=");
    }

    [Fact]
    public async Task Hosted_base64_decode_returns_binary_content()
    {
        var result = await RunNodeAsync<string, FlowContent>(
            SerializationComponentDefinition.Types.Base64Decode,
            "aGVsbG8=");

        var content = result.Value;
        content.ContentType.ShouldBe("application/octet-stream");
        content.Bytes.AsSpan()
            .SequenceEqual(Encoding.UTF8.GetBytes("hello")).ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_node_uses_optional_keyed_clock_for_events()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        await WithNodeAsync<FlowContent, JsonElement>(
            SerializationComponentDefinition.Types.JsonParse,
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<global::FluxFlow.Data.FlowValue>(Events, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(
                    FlowContent.FromBytes(
                        Encoding.UTF8.GetBytes("{}"),
                        "application/json")))).IsAccepted.ShouldBeTrue();

                var @event = (await receive).Message.ShouldNotBeNull().Value;
                @event.ToFlowEvent().Name.ShouldBe(SerializationDiagnosticNames.JsonParsed);
                @event.ToFlowEvent().Timestamp.ShouldBe(timestamp);
            },
            clock: clock);
    }

    [Fact]
    public async Task Hosted_expected_failure_uses_output_and_continues()
    {
        await WithNodeAsync<FlowContent, JsonElement>(
            SerializationComponentDefinition.Types.JsonParse,
            async (ports, _) =>
            {
                var bad = FlowMessage.Create(
                    FlowContent.FromBytes(Encoding.UTF8.GetBytes("{"), "application/json"),
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    FlowContent.FromBytes(Encoding.UTF8.GetBytes("{}"), "application/json"),
                    new CorrelationId("good"));

                var failureReceive = ports.ReceiveAsync<JsonElement>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, bad)).IsAccepted.ShouldBeTrue();
                var failure = (await failureReceive).Message.ShouldNotBeNull();

                var successReceive = ports.ReceiveAsync<JsonElement>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, good)).IsAccepted.ShouldBeTrue();
                var success = (await successReceive).Message.ShouldNotBeNull();
                failure.CorrelationId.ShouldBe(bad.CorrelationId);
                failure.Error.ShouldNotBeNull().Code
                    .ShouldBe(SerializationErrorCodeNames.JsonParseFailed);
                success.CorrelationId.ShouldBe(good.CorrelationId);
                success.IsError.ShouldBeFalse();
            });
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        await using var host = await StartHostAsync(
            SerializationComponentDefinition.Types.TextEncode,
            Properties(("boundedCapacity", 0)));

        AssertPreparationFailure(host, "boundedCapacity");
    }

    private static void AddSerialization(IServiceCollection services)
        => services.AddSerialization();

    private static async Task<FlowMessage<TOutput>> RunNodeAsync<TInput, TOutput>(
        string nodeType,
        TInput inputValue,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        FlowMessage<TOutput>? result = null;
        await WithNodeAsync<TInput, TOutput>(
            nodeType,
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<TOutput>(Output, Timeout);
                var message = FlowMessage.Create(inputValue, new CorrelationId(nodeType));
                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
                result = (await receive).Message.ShouldNotBeNull();
            },
            properties);
        return result.ShouldNotBeNull();
    }

    private static async Task WithNodeAsync<TInput, TOutput>(
        string nodeType,
        Func<ApplicationPorts, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null,
        TimeProvider? clock = null)
    {
        await using var host = await StartHostAsync(nodeType, properties, clock);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        string nodeType,
        IReadOnlyDictionary<string, object?>? properties = null,
        TimeProvider? clock = null)
    {
        var componentProperties = properties?.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal) ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        IReadOnlyList<string>? resources = null;
        if (clock is not null)
        {
            componentProperties[SerializationComponentDefinition.Resources.Clock] =
                "Resources.fixed";
            resources = ["fixed"];
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                nodeType,
                componentProperties,
                resources,
                componentName: "node", discardUnmatched: ["Output", "Events"]),
            AddSerialization,
            registerResources: context =>
            {
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("fixed"),
                        clock);
                }
            });
    }

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }
}
