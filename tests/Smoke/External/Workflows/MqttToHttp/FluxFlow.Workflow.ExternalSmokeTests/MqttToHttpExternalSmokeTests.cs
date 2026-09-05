using System.Text;
using System.Text.Json;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Mapping.Composition;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.MqttNet;
using FluxFlow.Components.Mqtt.Transport;
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

namespace FluxFlow.Workflow.ExternalSmokeTests;

public sealed class MqttToHttpExternalSmokeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [ExternalSmokeFact]
    [Trait("Category", "ExternalSmoke")]
    public async Task Configured_external_broker_routes_a_dynamic_workflow_to_a_real_http_boundary()
    {
        var settings = MqttToHttpExternalSettings.Load();
        await using var httpProbe = await HttpCaptureProbe.StartAsync();
        var scenarioId = Guid.NewGuid().ToString("N");
        var topic = $"fluxflow/external/{scenarioId}";

        var application = new ApplicationDefinitionBuilder();
        var broker = application.AddMqttBroker("broker", options =>
        {
            options.Host = settings.Host;
            options.Port = settings.Port;
            options.UseTls = settings.UseTls;
            options.Transport = MqttBrokerTransport.Tcp;
        });
        var subscription = application.AddMqttSubscription("scenario", options =>
        {
            options.TopicFilter = topic;
            options.Qos = MqttQos.AtLeastOnce;
        });
        var mqttClient = application.AddMqttClient("client", options =>
        {
            options.ClientId = $"fluxflow-external-{scenarioId}";
            options.Broker = broker;
            options.Username = settings.Username;
            options.Password = settings.Password;
            options.CleanStart = true;
            options.AutoConnect = MqttAutoConnectMode.OnStart;
            options.DisableReconnect();
            options.AddSubscription(subscription);
        });
        var expressionEngine = application.AddResource<IFlowExpressionEngine>(
            "expressionEngine",
            "host.expression");
        var httpClient = application.AddResource<HttpClient>("httpClient", "host.http-client");
        var workflow = application.AddWorkflow("crossTransport");
        var publish = workflow.AddMqttPublish("publish", options => options.Client = mqttClient);
        var receive = workflow.AddMqttReceive("receive", options =>
        {
            options.Client = mqttClient;
            options.AddSubscription(subscription);
        });
        var mapper = workflow.AddMapper("map", options =>
        {
            options.Engine = expressionEngine;
            options.Expression = $$"""
                {
                  'method': 'POST',
                  'url': '{{httpProbe.CaptureUrl.AbsoluteUri}}',
                  'headers': {
                    'x-flow-scenario': '{{scenarioId}}',
                    'x-mqtt-topic': input.topic
                  },
                  'body': {
                    'topic': input.topic,
                    'payload': input.content.bytes
                  }
                }
                """;
        });
        var request = workflow.AddHttpRequest("request", options => options.Client = httpClient);
        workflow.Connect(receive.Output, mapper.Input);
        workflow.Connect(mapper.Output, request.Input);

        using var outboundHttpClient = new HttpClient();
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            application.Build(),
            static _ => { },
            configureHostServices: services =>
                services.AddSingleton<IMqttTransportFactory, MqttNetTransportFactory>(),
            registerResources: context =>
            {
                context.Services.AddExternalFluxFlowResource<IFlowExpressionEngine>(
                    expressionEngine.Address,
                    new JsonataFlowExpressionEngine());
                context.Services.AddExternalFluxFlowResource<HttpClient>(
                    httpClient.Address,
                    outboundHttpClient);
            });
        host.StartResult.Succeeded.ShouldBeTrue(
            JsonSerializer.Serialize(host.StartResult.Update?.Diagnostics));

        var ports = host.GetRequiredPorts();
        var publishOutput = ports.ReceiveAsync<FlowValue>(publish.Output, Timeout);
        var httpOutput = ports.ReceiveAsync<FlowValue>(request.Output, Timeout);
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("{\"temperature\":21}"),
            "application/json",
            "utf-8");

        (await ports.SendAsync(
                publish.Input.Address,
                FlowMessage.Create(new MqttPublishMessage
                {
                    Topic = topic,
                    Content = content,
                    Qos = MqttQos.AtLeastOnce
                })))
            .IsAccepted.ShouldBeTrue();

        (await publishOutput).Message.ShouldNotBeNull().IsError.ShouldBeFalse();
        var captured = await httpProbe.ReceiveAsync(Timeout);
        (await httpOutput).Message.ShouldNotBeNull().IsError.ShouldBeFalse();
        captured.Scenario.ShouldBe(scenarioId);
        captured.Topic.ShouldBe(topic);
        using var body = JsonDocument.Parse(captured.Body);
        body.RootElement.GetProperty("topic").GetString().ShouldBe(topic);
        body.RootElement.GetProperty("payload").GetString().ShouldBe(
            Convert.ToBase64String(content.Bytes.AsSpan()));

        await host.Application.StopAsync();
    }
}
