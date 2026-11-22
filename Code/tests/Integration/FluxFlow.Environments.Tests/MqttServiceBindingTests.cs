using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography.X509Certificates;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class MqttServiceBindingTests
{
    [Theory]
    [InlineData("mqtt://service:1883", false)]
    [InlineData("mqtts://service:8883", true)]
    [InlineData("ws://service:8080/mqtt?tenant=trial", false)]
    [InlineData("wss://service:8443/mqtt?tenant=trial", true)]
    public void Service_endpoints_preserve_transport_path_and_security(string endpoint, bool secure)
    {
        var service = new InfrastructureService { Endpoint = endpoint, Mqtt = new()
        {
            Username = "service-user", Password = "service-password",
            TrustedCertificates = secure ? ["service-ca.pem"] : []
        } };
        var broker = JsonNode.Parse(Configuration)!["Resources"]!["broker"]!.AsObject();
        new MqttEnvironmentAdapter().ApplyExisting(broker, service);
        broker["Address"]!.GetValue<string>().ShouldBe(new Uri(endpoint).AbsoluteUri);
        broker["Tls"].ShouldBeNull();
        broker["Username"]!.GetValue<string>().ShouldBe("service-user");
        broker["TrustedCertificates"]!.AsArray().Count.ShouldBe(secure ? 1 : 0);
        broker["ClientCertificate"].ShouldBeNull();
        broker["ClientId"]!.GetValue<string>().ShouldBe("keep-client");
    }

    [Theory]
    [InlineData("mqtt://service/mqtt")]
    [InlineData("mqtts://service?tenant=trial")]
    [InlineData("wss://user:password@service/mqtt")]
    [InlineData("wss://service/mqtt#fragment")]
    [InlineData("https://service/mqtt")]
    public void Invalid_service_endpoints_do_not_partially_change_connection(string endpoint)
    {
        var broker = JsonNode.Parse(Configuration)!["Resources"]!["broker"]!.AsObject();
        var before = broker.ToJsonString();
        Should.Throw<ArgumentException>(() => new MqttEnvironmentAdapter().ApplyExisting(broker,
            new InfrastructureService { Endpoint = endpoint, Mqtt = new() }));
        broker.ToJsonString().ShouldBe(before);
    }

    [Theory]
    [InlineData("password", "Username")]
    [InlineData("client-certificate", "file paths")]
    [InlineData("private-key", "file paths")]
    [InlineData("trust", "file paths")]
    [InlineData("revocation", "revocation mode")]
    public void Provider_validation_rejects_invalid_overrides_before_changing_the_resource(string field, string message)
    {
        var settings = field switch
        {
            "password" => new MqttServiceSettings { Password = "not-for-display" },
            "client-certificate" => new() { ClientCertificate = new() { Path = " " } },
            "private-key" => new() { ClientCertificate = new() { Path = "client.pem", PrivateKeyPath = " " } },
            "trust" => new() { TrustedCertificates = [" "] },
            _ => new() { CertificateRevocationMode = (X509RevocationMode)999 }
        };
        var service = new InfrastructureService { Endpoint = "mqtts://host:8883", Mqtt = settings };
        var resource = JsonNode.Parse(Configuration)!["Resources"]!["broker"]!.AsObject();
        var before = resource.ToJsonString();
        service.Validate();
        var error = Should.Throw<ArgumentException>(() => new MqttEnvironmentAdapter().ApplyExisting(resource, service));
        error.Message.ShouldContain(message);
        error.Message.ShouldNotContain("not-for-display");
        resource.ToJsonString().ShouldBe(before);
    }

    [Theory]
    [InlineData("mqtt://host:1883", false, false)]
    [InlineData("ws://host/mqtt", false, false)]
    [InlineData("mqtt://host:1883", true, true)]
    [InlineData("wss://host/mqtt", false, true)]
    public async Task Check_validates_the_bound_transport_against_the_effective_certificates(string endpoint, bool clear, bool valid)
    {
        var service = new InfrastructureService { Endpoint = endpoint, Mqtt = clear ? new() : null };
        var request = new EnvironmentRunRequest(Configuration, new()) { Instances = [new("bound", "App")
            { ExistingServices = new() { ["Resources:broker"] = service } }] };
        var adapter = new MqttEnvironmentAdapter();
        var check = await new EnvironmentCheck(new EnvironmentPreparation([adapter], []), []).CheckAsync(request, default);
        check.Success.ShouldBe(valid);
        if (!valid)
        {
            check.NodeId.ShouldBe("bound");
            check.Message.ShouldContain("certificates");
            check.Message.ShouldNotContain("manual-password");
            var broker = JsonNode.Parse(Configuration)!["Resources"]!["broker"]!.AsObject();
            var original = broker.ToJsonString();
            Should.Throw<ArgumentException>(() => adapter.ApplyExisting(broker, service));
            broker.ToJsonString().ShouldBe(original);
        }
    }

    private const string Configuration = """
        {"Resources":{"broker":{"Type":"mqtt.client","Host":"manual","Port":8883,"Tls":true,
        "Username":"manual-user","Password":"manual-password","ClientId":"keep-client",
        "ClientCertificate":{"Path":"manual.pfx"},"TrustedCertificates":["manual-ca.pem"],
        "CleanSession":false,"Reconnect":{"Delay":"00:00:07"}}},"Workflows":{"main":{}}}
        """;

    [Fact]
    public async Task Service_settings_use_the_existing_mqtt_contract_without_changing_manual_connections_or_other_instances()
    {
        var service = new InfrastructureService { Endpoint = "mqtts://service:8883", Mqtt = new()
        {
            Username = "service-user", Password = "service-password",
            ClientCertificate = new() { Path = "client.pem", PrivateKeyPath = "client-key.pem", Password = "key-password" },
            TrustedCertificates = ["service-ca.pem"], CertificateRevocationMode = X509RevocationMode.Offline
        } };
        var request = new EnvironmentRunRequest(Configuration, new()) { Instances =
        [new("bound", "Bound") { ExistingServices = new() { ["resources:BROKER"] = service } }, new("manual", "Manual")] };
        var original = JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        request = JsonSerializer.Deserialize(original, EnvironmentJsonContext.Default.EnvironmentRunRequest)!;
        var preparation = new EnvironmentPreparation([new MqttEnvironmentAdapter()], []);
        preparation.ValidateBindings(request);
        await using var prepared = await preparation.PrepareAsync(Configuration, new());
        var generated = JsonNode.Parse(preparation.BindConfiguration(prepared, request.Instances[0]))!;
        var broker = generated["Resources"]!["broker"]!;
        broker["Address"]!.GetValue<string>().ShouldBe("mqtts://service:8883/");
        broker["Host"].ShouldBeNull(); broker["Port"].ShouldBeNull(); broker["Tls"].ShouldBeNull();
        broker["Username"]!.GetValue<string>().ShouldBe("service-user");
        broker["Password"]!.GetValue<string>().ShouldBe("service-password");
        broker["ClientCertificate"]!["Path"]!.GetValue<string>().ShouldBe("client.pem");
        broker["ClientCertificate"]!["PrivateKeyPath"]!.GetValue<string>().ShouldBe("client-key.pem");
        broker["ClientCertificate"]!["Password"]!.GetValue<string>().ShouldBe("key-password");
        broker["TrustedCertificates"]![0]!.GetValue<string>().ShouldBe("service-ca.pem");
        broker["CertificateRevocationMode"]!.GetValue<string>().ShouldBe("Offline");
        broker["ClientId"]!.GetValue<string>().ShouldBe("keep-client");
        broker["CleanSession"]!.GetValue<bool>().ShouldBeFalse();
        broker["Reconnect"]!["Delay"]!.GetValue<string>().ShouldBe("00:00:07");
        broker["Mqtt"].ShouldBeNull();
        JsonNode.DeepEquals(JsonNode.Parse(preparation.BindConfiguration(prepared, request.Instances[1])), JsonNode.Parse(Configuration)).ShouldBeTrue();
        JsonNode.DeepEquals(prepared.Configuration, JsonNode.Parse(Configuration)).ShouldBeTrue();
        JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_bindings_inherit_manual_security_but_explicit_empty_service_settings_clear_it(bool serviceSettings)
    {
        var broker = JsonNode.Parse(Configuration)!["Resources"]!["broker"]!.AsObject();
        new MqttEnvironmentAdapter().ApplyExisting(broker, new InfrastructureService
        { Endpoint = "mqtts://service:8883", Mqtt = serviceSettings ? new() : null });
        if (serviceSettings)
        {
            broker["Username"].ShouldBeNull(); broker["Password"].ShouldBeNull(); broker["ClientCertificate"].ShouldBeNull();
            broker["TrustedCertificates"]!.AsArray().Count.ShouldBe(0);
            broker["CertificateRevocationMode"]!.GetValue<string>().ShouldBe("Online");
        }
        else
        {
            broker["Username"]!.GetValue<string>().ShouldBe("manual-user");
            broker["Password"]!.GetValue<string>().ShouldBe("manual-password");
            broker["ClientCertificate"]!["Path"]!.GetValue<string>().ShouldBe("manual.pfx");
            broker["TrustedCertificates"]![0]!.GetValue<string>().ShouldBe("manual-ca.pem");
        }
    }

    [Fact]
    public async Task Invalid_service_security_is_reported_before_preparation_without_exposing_credentials()
    {
        var preparation = new EnvironmentPreparation([new MqttEnvironmentAdapter()], []);
        var request = new EnvironmentRunRequest(Configuration, new()) { Instances = [new("app", "App")
        { ExistingServices = new() { ["Resources:broker"] = new() { Mqtt = new() { Password = "private-value" } } } }] };
        var result = await new EnvironmentCheck(preparation, []).CheckAsync(request, CancellationToken.None);
        result.Success.ShouldBeFalse(); result.NodeId.ShouldBe("app");
        result.Message.ShouldContain("Username"); result.Message.ShouldNotContain("private-value");
        var insecure = new InfrastructureService { Mqtt = new() { TrustedCertificates = ["ca.pem"] } };
        insecure.Validate(); // Without its plugin the host can still open and save the service.
        Should.Throw<ArgumentException>(() => insecure.Validate(new MqttEnvironmentAdapter().Infrastructure));
        Should.Throw<ArgumentException>(() => new InfrastructureService { Type = "http.client", Endpoint = "https://api/", Mqtt = new() }.Validate());
    }
}
