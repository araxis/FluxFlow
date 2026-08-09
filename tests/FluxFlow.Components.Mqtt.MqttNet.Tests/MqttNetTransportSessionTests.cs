using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Data;
using MQTTnet;
using MQTTnet.Protocol;
using Shouldly;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace FluxFlow.Components.Mqtt.MqttNet.Tests;

public sealed class MqttNetTransportSessionTests
{
    [Theory]
    [InlineData(MqttBrokerTransport.Tcp, false, "tcp")]
    [InlineData(MqttBrokerTransport.Tcp, true, "tls")]
    [InlineData(MqttBrokerTransport.WebSocket, false, "ws")]
    [InlineData(MqttBrokerTransport.WebSocket, true, "wss")]
    public async Task Session_maps_all_four_portable_modes_to_exact_mqttnet_channel_options(
        MqttBrokerTransport transport,
        bool useTls,
        string expectedScheme)
    {
        using var certificate = CreateCertificate();
        var broker = new MqttBrokerConfiguration
        {
            Host = transport == MqttBrokerTransport.WebSocket
                ? "2001:db8::1"
                : "broker.internal",
            Port = 9443,
            Transport = transport,
            UseTls = useTls,
            ServerName = transport == MqttBrokerTransport.Tcp
                ? "sni.internal"
                : null,
            WebSocketPath = transport == MqttBrokerTransport.WebSocket
                ? "/tenant mqtt"
                : "/mqtt"
        };
        var configuration = Configuration(broker) with
        {
            Certificates =
            [
                new MqttClientCertificate
                {
                    Name = "client.cer",
                    Content = certificate.Export(X509ContentType.Cert)
                }
            ]
        };
        var provider = new VNextRecordingMqttNetClient();
        await using var session = new MqttNetTransportSession(
            configuration,
            new MqttClientFactory(),
            provider,
            TimeProvider.System);

        var options = session.BuildClientOptions();

        MqttClientTlsOptions? tls;
        if (transport == MqttBrokerTransport.Tcp)
        {
            var tcp = options.ChannelOptions.ShouldBeOfType<MqttClientTcpOptions>();
            var endpoint = tcp.RemoteEndpoint.ShouldBeOfType<DnsEndPoint>();
            endpoint.Host.ShouldBe("broker.internal");
            endpoint.Port.ShouldBe(9443);
            tls = tcp.TlsOptions;
        }
        else
        {
            var webSocket = options.ChannelOptions
                .ShouldBeOfType<MqttClientWebSocketOptions>();
            var endpoint = new Uri(webSocket.Uri, UriKind.Absolute);
            endpoint.AbsoluteUri.ShouldBe(
                $"{expectedScheme}://[2001:db8::1]:9443/tenant%20mqtt");
            webSocket.SubProtocols.ShouldBe(["mqtt"], ignoreOrder: false);
            tls = webSocket.TlsOptions;
        }

        if (useTls)
        {
            var secureTls = tls.ShouldNotBeNull();
            secureTls.UseTls.ShouldBeTrue();
            secureTls.TargetHost.ShouldBe(
                transport == MqttBrokerTransport.Tcp
                    ? "sni.internal"
                    : "2001:db8::1");
            secureTls.ClientCertificatesProvider.ShouldNotBeNull()
                .GetCertificates()
                .Cast<X509Certificate2>()
                .ShouldHaveSingleItem()
                .Thumbprint.ShouldBe(certificate.Thumbprint);
        }
        else if (transport == MqttBrokerTransport.WebSocket)
        {
            tls.ShouldBeNull();
        }
        else
        {
            var plainTls = tls.ShouldNotBeNull();
            plainTls.UseTls.ShouldBeFalse();
            plainTls.ClientCertificatesProvider.ShouldBeNull();
        }
    }

    [Fact]
    public async Task SessionMapsConfigurationPublishAndSubscriptionsWithoutOwningPolicy()
    {
        var provider = new VNextRecordingMqttNetClient();
        var factory = new MqttNetTransportFactory(
            new MqttClientFactory(),
            () => provider);
        await using var session = await factory.CreateAsync(Configuration());

        await session.ConnectAsync();
        await session.PublishAsync(new MqttPublishMessage
        {
            Topic = "events/one",
            Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }, "application/octet-stream"),
            Qos = MqttQos.AtLeastOnce,
            CorrelationData = "correlation-1",
            ResponseTopic = "responses/one",
            UserProperties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tenant"] = "north"
            }
        });
        await session.SubscribeAsync("name:events", new MqttSubscriptionDefinition
        {
            TopicFilter = "events/+",
            Qos = MqttQos.AtLeastOnce,
            NoLocal = true,
            RetainAsPublished = true,
            RetainHandling =
                FluxFlow.Components.Mqtt.Subscriptions.MqttRetainHandling.SendOnNewSubscription
        });
        await session.UnsubscribeAsync("name:events");

        provider.Options.ClientId.ShouldBe("client-1");
        provider.Options.CleanSession.ShouldBeFalse();
        provider.Options.KeepAlivePeriod.ShouldBe(TimeSpan.FromSeconds(20));
        ((DnsEndPoint)((MqttClientTcpOptions)provider.Options.ChannelOptions).RemoteEndpoint)
            .Host.ShouldBe("broker.internal");
        provider.Published!.Topic.ShouldBe("events/one");
        provider.Published.Payload.FirstSpan.ToArray().ShouldBe([1, 2, 3]);
        provider.Published.QualityOfServiceLevel.ShouldBe(MqttQualityOfServiceLevel.AtLeastOnce);
        provider.Published.ResponseTopic.ShouldBe("responses/one");
        provider.Subscribed!.TopicFilters.Single().Topic.ShouldBe("events/+");
        provider.Unsubscribed!.TopicFilters.ShouldBe(["events/+"], ignoreOrder: false);
    }

    [Fact]
    public async Task SessionDefersQosDeliveryAndCompletesProviderAcknowledgementOnce()
    {
        var provider = new VNextRecordingMqttNetClient();
        var factory = new MqttNetTransportFactory(
            new MqttClientFactory(),
            () => provider);
        await using var session = await factory.CreateAsync(Configuration());
        await session.ConnectAsync();
        var next = ReadNextAsync(session.Messages);

        await provider.ReceiveAsync(new MqttApplicationMessageBuilder()
            .WithTopic("events/one")
            .WithPayload([4, 5, 6])
            .WithContentType("application/octet-stream")
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build());
        var received = await next.WaitAsync(TimeSpan.FromSeconds(5));

        received.Message.Topic.ShouldBe("events/one");
        received.Message.Content.Bytes.ToArray().ShouldBe([4, 5, 6]);
        received.Delivery.IsEmpty.ShouldBeFalse();
        provider.LastReceived!.AutoAcknowledge.ShouldBeFalse();

        await session.AcknowledgeAsync(received.Delivery, MqttWorkflowOutcome.Nak);
        await session.AcknowledgeAsync(received.Delivery, MqttWorkflowOutcome.Ack);

        provider.AcknowledgementCount.ShouldBe(1);
        provider.LastReceived.ProcessingFailed.ShouldBeTrue();
    }

    [Fact]
    public async Task FactoryHonorsCancellationBeforeCreatingSession()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new MqttNetTransportFactory();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await factory.CreateAsync(Configuration(), cancellation.Token));
    }

    [Fact]
    public async Task ConnectRejectionPreservesAuthenticationClassification()
    {
        var provider = new VNextRecordingMqttNetClient
        {
            ConnectResult = new MqttClientConnectResult
            {
                ResultCode = MqttClientConnectResultCode.NotAuthorized,
                ReasonString = "blocked"
            }
        };
        var factory = new MqttNetTransportFactory(
            new MqttClientFactory(),
            () => provider);
        await using var session = await factory.CreateAsync(Configuration());

        var exception = await Should.ThrowAsync<MqttTransportException>(async () =>
            await session.ConnectAsync());

        exception.Category.ShouldBe("Authentication");
        exception.IsTransient.ShouldBeFalse();
        session.IsConnected.ShouldBeFalse();
    }

    private static MqttClientConfiguration Configuration(
        MqttBrokerConfiguration? broker = null)
        => new()
        {
            Name = "client-1",
            ClientId = "client-1",
            Broker = broker ?? new MqttBrokerConfiguration
            {
                Host = "broker.internal",
                Port = 1884
            },
            CleanStart = false,
            KeepAlive = TimeSpan.FromSeconds(20),
            AutoConnect = MqttAutoConnectMode.Disabled
        };

    private static async Task<T> ReadNextAsync<T>(IAsyncEnumerable<T> source)
    {
        await foreach (var value in source)
            return value;
        throw new InvalidOperationException("The MQTT transport stream completed without a value.");
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=FluxFlow MQTT transport test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }
}
