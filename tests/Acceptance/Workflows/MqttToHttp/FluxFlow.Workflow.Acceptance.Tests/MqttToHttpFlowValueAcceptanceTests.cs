using System.Net;
using System.Text;
using System.Text.Json;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Data;
using FluxFlow.Expressions.Jsonata;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Workflow.Acceptance.Tests;

public sealed class MqttToHttpFlowValueAcceptanceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    [Trait("Category", "Acceptance")]
    public async Task Authored_mqtt_value_maps_through_jsonata_to_http_and_preserves_flow_context()
    {
        var builder = new ApplicationDefinitionBuilder();
        var engine = builder.AddResource<IFlowExpressionEngine>("mappingEngine", "host.expression");
        var client = builder.AddResource<HttpClient>("httpClient", "host.http-client");
        var workflow = builder.AddWorkflow("ingress");
        var mapper = workflow.AddMapper("mapMqttMessage", options =>
        {
            options.Engine = engine;
            options.Expression = """
                $type(input) = 'object'
                  ? {
                      'method': 'POST',
                      'url': 'https://collector.test/readings',
                      'headers': { 'x-mqtt-topic': input.topic },
                      'body': {
                        'topic': input.topic,
                        'payload': input.content.bytes
                      }
                    }
                  : input
                """;
        });
        var request = workflow.AddHttpRequest("sendHttpRequest", options => options.Client = client);
        workflow.Connect(mapper.Output, request.Input);

        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            builder.Build(),
            services => services.AddFluxFlowComponents().AddMapping().AddHttp(),
            registerResources: context =>
            {
                context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    engine.Address,
                    new JsonataFlowExpressionEngine());
                context.Services.AddExternalFluxFlowResource<HttpClient>(client.Address, httpClient);
            });

        host.StartResult.Succeeded.ShouldBeTrue(
            JsonSerializer.Serialize(host.StartResult.Update?.Diagnostics));
        var ports = host.GetRequiredPorts();

        var invalidOutput = ports.ReceiveAsync<FlowValue>(request.Output, Timeout);
        var invalidInput = FlowMessage.Create(FlowValue.From(42));
        (await ports.SendAsync(request.Input, invalidInput)).IsAccepted.ShouldBeTrue();
        var invalid = (await invalidOutput).Message.ShouldNotBeNull();
        invalid.IsError.ShouldBeTrue();
        invalid.Error.ShouldNotBeNull().Code.ShouldBe("http.request.invalid_shape");
        handler.CallCount.ShouldBe(0);

        var mappedOutput = ports.ReceiveAsync<FlowValue>(mapper.Output, Timeout);
        var httpOutput = ports.ReceiveAsync<FlowValue>(request.Output, Timeout);
        var mqttMessage = new MqttReceivedApplicationMessage
        {
            Timestamp = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
            Topic = "devices/alpha/temperature",
            Content = FlowContent.FromBytes(
                Encoding.UTF8.GetBytes("{\"temperature\":21}"),
                "application/json",
                "utf-8")
        };
        var input = FlowMessage.Create(
            mqttMessage,
            headers: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tenant"] = "north"
            });

        (await ports.SendAsync(mapper.Input.Address, input)).IsAccepted.ShouldBeTrue();

        var mapped = (await mappedOutput).Message.ShouldNotBeNull();
        var response = (await httpOutput).Message.ShouldNotBeNull();
        mapped.IsError.ShouldBeFalse();
        response.IsError.ShouldBeFalse();
        mapped.TraceId.ShouldBe(input.TraceId);
        mapped.CausationId.ShouldBe(input.MessageId);
        mapped.Headers["tenant"].ShouldBe("north");
        response.TraceId.ShouldBe(input.TraceId);
        response.CausationId.ShouldBe(mapped.MessageId);
        response.Headers["tenant"].ShouldBe("north");

        handler.CallCount.ShouldBe(1);
        handler.Method.ShouldBe(HttpMethod.Post);
        handler.Url.ShouldBe("https://collector.test/readings");
        handler.MqttTopic.ShouldBe(mqttMessage.Topic);
        using var body = JsonDocument.Parse(handler.Body.ShouldNotBeNull());
        body.RootElement.GetProperty("topic").GetString().ShouldBe(mqttMessage.Topic);
        body.RootElement.GetProperty("payload").GetString().ShouldBe(
            Convert.ToBase64String(mqttMessage.Content.Bytes.AsSpan()));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string? Url { get; private set; }

        public string? MqttTopic { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            Url = request.RequestUri?.AbsoluteUri;
            MqttTopic = request.Headers.GetValues("x-mqtt-topic").Single();
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    "{\"accepted\":true}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
