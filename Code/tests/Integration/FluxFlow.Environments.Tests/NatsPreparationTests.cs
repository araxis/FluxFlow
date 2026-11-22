using System.Text.Json.Nodes;
using FluxFlow.Environments.Testcontainers;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class NatsPreparationTests
{
    [Fact]
    public void Managed_credentials_replace_only_the_snapshot_and_existing_bindings_keep_tls()
    {
        var original = JsonNode.Parse("""{"Endpoint":"tls://production:4222","TlsMode":"Require","CredentialsFileEnvironmentVariable":"PRODUCTION_CREDS","CaFileEnvironmentVariable":"PRODUCTION_CA","TimeoutSeconds":17}""")!.AsObject();
        var saved = original.ToJsonString();
        var resource = original.DeepClone().AsObject();
        var adapter = new NatsEnvironmentAdapter();
        adapter.Apply(resource, new Uri("nats://localhost:54321"), new() { Variables = new() { ["NATS_TOKEN_ENVIRONMENT_VARIABLE"] = "TRIAL_TOKEN" } });
        resource.ContainsKey("CredentialsFileEnvironmentVariable").ShouldBeFalse();
        resource.ContainsKey("CaFileEnvironmentVariable").ShouldBeFalse();
        resource["TlsMode"]!.GetValue<string>().ShouldBe("Disable");
        resource["TokenEnvironmentVariable"]!.GetValue<string>().ShouldBe("TRIAL_TOKEN");
        resource["TimeoutSeconds"]!.GetValue<int>().ShouldBe(17);
        var existing = original.DeepClone().AsObject();
        adapter.ApplyExisting(existing, new Uri("tls://another-host:4222"));
        existing["CredentialsFileEnvironmentVariable"]!.GetValue<string>().ShouldBe("PRODUCTION_CREDS");
        existing["TlsMode"]!.GetValue<string>().ShouldBe("Require");
        original.ToJsonString().ShouldBe(saved);
    }
    [Theory]
    [InlineData("NATS_TOKEN", "secret")]
    [InlineData("NATS_PASSWORD", "secret")]
    [InlineData("NATS_TOKEN_ENVIRONMENT_VARIABLE", "")]
    [InlineData("NATS_USERNAME_ENVIRONMENT_VARIABLE", "MISSING_PAIR")]
    public void Invalid_credentials_are_rejected_before_container_creation(string key, string value)
        => Should.Throw<ArgumentException>(() => new ConfiguredContainerProvider(FluxFlow.Plugins.Nats.NatsContainer.Definition).Validate(new() { Variables = new() { [key] = value } }));
}
