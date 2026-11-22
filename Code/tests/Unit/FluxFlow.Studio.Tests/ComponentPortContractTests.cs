using System.Text;
using System.Text.Json.Nodes;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.DataGeneration;
using FluxFlow.Configuration.Components.Filtering;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Logging;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Configuration.Components.RateLimiting;
using FluxFlow.Configuration.Components.RequestReply;
using FluxFlow.Configuration.Components.Reducing;
using FluxFlow.Configuration.Components.Resilience;
using FluxFlow.Configuration.Components.Ros2;
using FluxFlow.Configuration.Components.Routing;
using FluxFlow.Configuration.Components.Serial;
using FluxFlow.Hosting.AspNetCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rosidl.Runtime;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ComponentPortContractTests
{
    [Fact]
    public void Added_descriptions_match_registered_ports_without_activating_components()
    {
        const string json = """
            {"Workflows":{"main":{
              "Resources":{"serial":{"Type":"serial.port","PortName":"unused"}},
              "filter":{"Type":"changeFilter","Engine":"unavailable","KeySelector":"missing.jsonata"},
              "dedup":{"Type":"deduplicate","Engine":"unavailable","KeySelector":"missing.jsonata","Retention":"00:01:00"},
              "generate":{"Type":"dataGenerator","Expression":"missing.jsonata"},
              "limit":{"Type":"rateLimit","Limiter":"missing"},
              "exchange":{"Type":"requestReply"},
              "retry":{"Type":"retry"},
              "reduce":{"Type":"reducer","Engine":"unavailable","InitialState":"0","Reduce":"missing.jsonata","AreEqual":"missing.jsonata"},
              "route":{"Type":"router","Engine":"unavailable","Routes":{"Next":"missing.jsonata"}},
              "http":{"Type":"http.client","Client":"missing"},
              "connection":{"Type":"mqtt.connection","Connection":"missing"},
              "publisher":{"Type":"mqtt.publisher","Connection":"missing"},
              "subscriber":{"Type":"mqtt.subscriber","Connection":"missing","Topics":["sensors/#"]},
              "log":{"Type":"logger","Logger":"missing"},
              "serial":{"Type":"serial.text","Connection":"serial"},
              "rosPublisher":{"Type":"ros.publisher","Node":"missing","Topic":"/telemetry"},
              "rosSubscriber":{"Type":"ros.subscriber","Node":"missing","Topic":"/telemetry"},
              "trigger":{"Type":"http.trigger","Route":"/telemetry"}
            }}}
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
        var services = new ServiceCollection()
            .AddChangeFilter().AddDeduplicate().AddDataGenerator().AddRateLimit().AddRequestReply().AddRetry().AddReducer().AddRouter()
            .AddHttp().AddMqtt().AddLogger().AddSerial().AddRosPublisher<RegistrationMessage>()
            .AddRosSubscriber<RegistrationMessage>().AddHttpTriggers().AddFluxFlowApplication(configuration);
        using var provider = services.BuildServiceProvider();
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(json);
        graph.NodeCount.ShouldBe(17);
        var document = new DesignerConfiguration(json);
        foreach (var node in document.Nodes("main"))
        {
            var palette = graph.FindDescription(node.Type)!.GetPresentation(node.Values);
            provider.GetInputPortAddresses().Where(port => port.ComponentAddress == node.Address)
                .Select(port => port.PortName).Order().ShouldBe(palette.Inputs.Select(port => port.Name).Order());
            provider.GetOutputPortAddresses().Where(port => port.ComponentAddress == node.Address)
                .Select(port => port.PortName).Order().ShouldBe(palette.Outputs.Select(port => port.Name).Order());
        }
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(graph.ToJson())).ShouldBeTrue();
    }

    // Registration requires a message type but must never ask it for native type support.
    private sealed class RegistrationMessage : IMessage
    {
        public static string TypeSupportName => "tests/Registration";
        public static TypeSupportHandle GetTypeSupportHandle() => throw new NotSupportedException();
        public static IMessage CreateFrom(nint data, Encoding encoding) => throw new NotSupportedException();
        public static nint UnsafeCreate() => throw new NotSupportedException();
        public static void UnsafeDestroy(nint data) => throw new NotSupportedException();
        public static bool UnsafeInitialize(nint data) => throw new NotSupportedException();
        public static void UnsafeFinalize(nint data) => throw new NotSupportedException();
        public static bool UnsafeInitializeSequence(int size, nint data) => throw new NotSupportedException();
        public static void UnsafeFinalizeSequence(nint data) => throw new NotSupportedException();
        public void WriteTo(nint data, Encoding encoding) => throw new NotSupportedException();
    }
}
