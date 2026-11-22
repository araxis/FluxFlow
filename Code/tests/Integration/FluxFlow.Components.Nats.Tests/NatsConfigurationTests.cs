using System.Text;
using FluxFlow.Configuration.Components.Nats;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed class NatsConfigurationTests
{
    [Fact]
    public void Saved_numeric_options_map_to_the_same_native_modes()
    {
        AssertModes<NatsTlsMode, TlsMode>();
        AssertModes<JetStream.JetStreamStorage, global::NATS.Client.JetStream.Models.StreamConfigStorage>();
        AssertModes<JetStream.JetStreamDeliverPolicy, global::NATS.Client.JetStream.Models.ConsumerConfigDeliverPolicy>();

        static void AssertModes<TSettings, TDriver>() where TSettings : struct, Enum where TDriver : struct, Enum
        {
            foreach (var mode in Enum.GetValues<TSettings>())
                Enum.GetName(typeof(TDriver), Convert.ToInt32(mode)).ShouldBe(mode.ToString());
        }
    }

    [Theory]
    [InlineData("nats://user:password@localhost")]
    [InlineData("https://localhost")]
    [InlineData("nats://localhost/topic")]
    [InlineData("nats://localhost:0")]
    public void Endpoint_rejects_secrets_paths_and_wrong_protocols(string endpoint)
        => Should.Throw<ArgumentException>(() => new NatsConnectionConfiguration { Endpoint = endpoint }.Validate());

    [Theory]
    [InlineData("Password")]
    [InlineData("Token")]
    [InlineData("Seed")]
    [InlineData("AuthOpts")]
    public void Inline_credentials_are_rejected(string key)
    {
        using var configuration = Read("{\"" + key + "\":\"secret\"}");
        Should.Throw<ArgumentException>(() => NatsConnectionConfiguration.Read(configuration.GetSection("Connection")));
    }

    [Fact]
    public void Host_references_validate_without_resolving_and_resolve_only_at_execution()
    {
        var name = "NATS_TEST_" + Guid.NewGuid().ToString("N");
        var config = new NatsConnectionConfiguration { TokenEnvironmentVariable = name };
        config.Validate();
        Should.Throw<InvalidOperationException>(() => config.CreateOptions(NullLoggerFactory.Instance));
        try
        {
            Environment.SetEnvironmentVariable(name, "host-owned-token");
            var options = config.CreateOptions(NullLoggerFactory.Instance);
            options.AuthOpts.Token.ShouldBe("host-owned-token");
            options.LoggerFactory.ShouldBeSameAs(NullLoggerFactory.Instance);
        }
        finally { Environment.SetEnvironmentVariable(name, null); }
    }

    [Fact]
    public void Authentication_tls_and_reconnect_limits_are_explicit()
    {
        Should.Throw<ArgumentException>(() => new NatsConnectionConfiguration { UsernameEnvironmentVariable = "USER_ONLY" }.Validate());
        Should.Throw<ArgumentException>(() => new NatsConnectionConfiguration { TokenEnvironmentVariable = "TOKEN", CredentialsFileEnvironmentVariable = "CREDS" }.Validate());
        Should.Throw<ArgumentException>(() => new NatsConnectionConfiguration { Endpoint = "tls://localhost", TlsMode = NatsTlsMode.Prefer }.Validate());
        Should.Throw<ArgumentException>(() => new NatsConnectionConfiguration { TlsMode = NatsTlsMode.Disable, CaFileEnvironmentVariable = "CA" }.Validate());
        Should.Throw<ArgumentException>(() => new NatsConnectionConfiguration { MaxReconnectAttempts = 0 }.Validate());
        new NatsConnectionConfiguration { Endpoint = "tls://localhost", TlsMode = NatsTlsMode.Require }.Validate();
    }

    private static ConfigurationRoot Read(string json) => (ConfigurationRoot)new ConfigurationBuilder()
        .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("{\"Connection\":" + json + "}"))).Build();
}
