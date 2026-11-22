using System.Text.Json.Nodes;
using FluxFlow.Environments.Testcontainers;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class PreparationTests
{
    [Fact]
    public async Task Instance_bindings_use_prepared_service_settings_without_mutating_snapshots()
    {
        const string configuration = """{"Resources":{"one":{"Type":"mqtt.client","Address":"mqtt://one:1883"},"two":{"Type":"mqtt.client","Address":"mqtt://two:1883"},"api":{"Type":"http.client","BaseAddress":"http://api/"}},"Workflows":{"main":{}}}""";
        var preparation = Create();
        var instance = new ApplicationInstance("app", "Application") { Bindings = new() { ["resources:ONE"] = "Resources:two", ["Resources:two"] = "Resources:one" } };
        var request = new EnvironmentRunRequest(configuration, new()) { Instances = [instance] };
        var legacy = System.Text.Json.JsonSerializer.SerializeToNode(request, EnvironmentJsonContext.Default.EnvironmentRunRequest)!;
        legacy["Instances"]![0]!.AsObject().Remove("Bindings");
        var legacyRequest = System.Text.Json.JsonSerializer.Deserialize(legacy.ToJsonString(), EnvironmentJsonContext.Default.EnvironmentRunRequest)!;
        legacyRequest.Instances.Single().Bindings.ShouldBeEmpty();
        preparation.ValidateBindings(legacyRequest);
        preparation.ValidateBindings(request);
        await using var prepared = await preparation.PrepareAsync(configuration, request.Environment);
        var original = prepared.Configuration.ToJsonString();
        var bound = JsonNode.Parse(preparation.BindConfiguration(prepared, instance))!;
        bound["Resources"]!["one"]!["Address"]!.GetValue<string>().ShouldBe("mqtt://two:1883");
        bound["Resources"]!["two"]!["Address"]!.GetValue<string>().ShouldBe("mqtt://one:1883");
        prepared.Configuration.ToJsonString().ShouldBe(original);
        request.Configuration.ShouldBe(configuration);
        instance.Bindings["resources:ONE"] = "Resources:api";
        var check = await new EnvironmentCheck(preparation, []).CheckAsync(request, CancellationToken.None);
        check.Success.ShouldBeFalse();
        check.Message.ShouldContain("compatible");
        check.NodeId.ShouldBe("app");
        instance.Bindings["resources:ONE"] = "Resources:missing";
        Should.Throw<ArgumentException>(() => preparation.ValidateBindings(request));
    }

    [Fact]
    public async Task Check_identifies_resource_failures_without_changing_validation_or_configuration()
    {
        var preparation = Create();
        var request = new EnvironmentRunRequest(Configuration, new()
        {
            Resources = new() { ["Resources:Broker"] = new() { Provider = "missing-provider" } }
        });
        var original = System.Text.Json.JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        var error = Should.Throw<ArgumentException>(() => preparation.Validate(request.Configuration, request.Environment));
        var result = await new EnvironmentCheck(preparation, []).CheckAsync(request, CancellationToken.None);
        result.Success.ShouldBeFalse();
        result.Message.ShouldBe(error.Message);
        result.Resource.ShouldBe("Resources:Broker");
        result.NodeId.ShouldBeNull();
        var transported = System.Text.Json.JsonSerializer.Deserialize(
            System.Text.Json.JsonSerializer.Serialize(result, EnvironmentJsonContext.Default.EnvironmentCheckResult),
            EnvironmentJsonContext.Default.EnvironmentCheckResult)!;
        transported.ShouldBe(result);
        System.Text.Json.JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(original);
        request.Environment.StartupTimeoutSeconds = 0;
        result = await new EnvironmentCheck(preparation, []).CheckAsync(request, CancellationToken.None);
        result.Resource.ShouldBeNull();
        result.NodeId.ShouldBeNull();
    }

    [Theory]
    [InlineData("mosquitto", "/mosquitto/config/acl")]
    [InlineData("rabbitmq-mqtt", "/etc/rabbitmq/definitions.json")]
    public void Visual_broker_setup_rejects_conflicting_files_and_duplicate_users(string name, string path)
    {
        IEnvironmentProvider provider = name == "mosquitto" ? new ConfiguredContainerProvider(FluxFlow.Plugins.Mqtt.MosquittoContainer.Definition) : new ConfiguredContainerProvider(FluxFlow.Plugins.Mqtt.RabbitMqMqttContainer.Definition);
        var access = new BrokerAccess { Enabled = true, ClientUsername = "reader", Users = [new() { Name = "reader", Password = "test" }] };
        var binding = new ResourceBinding { Provider = name, BrokerAccess = new() { [name] = access } };
        provider.Validate(binding);
        Should.Throw<ArgumentException>(() => provider.Validate(binding with { Files = new() { [path] = "custom" } })).Message.ShouldContain("User settings conflict");
        Should.Throw<ArgumentException>(() => provider.Validate(binding with { Files = new() { [path.Replace("/", "//")] = "custom" } }));
        Should.Throw<ArgumentException>(() => provider.Validate(binding with { BrokerAccess = new() { [name] = access with { Users = [.. access.Users, .. access.Users] } } })).Message.ShouldContain("unique");
        Should.Throw<ArgumentException>(() => provider.Validate(binding with { BrokerAccess = new() { [name] = access with { ClientUsername = "missing" } } })).Message.ShouldContain("Select");
    }

    [Fact]
    public void Malformed_saved_users_are_rejected_before_the_editor_reads_them()
    {
        var profile = System.Text.Json.JsonSerializer.Deserialize("""
            {"Resources":{"Resources:broker":{"Provider":"mosquitto","BrokerAccess":{"mosquitto":{"Users":[null]}}}}}
            """, EnvironmentJsonContext.Default.EnvironmentProfile)!;
        Should.Throw<ArgumentException>(() => profile.Validate());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mqtt_run_credentials_are_separate_from_the_original_configuration_and_endpoint(bool visual)
    {
        var resource = JsonNode.Parse("""{"Address":"mqtt://original:1883","Username":"original","Password":"original-secret","ClientId":"KeepCase"}""")!.AsObject();
        var original = resource.ToJsonString();
        var copy = resource.DeepClone().AsObject();
        var binding = new ResourceBinding { Provider = "mosquitto", BrokerAccess = new() { ["mosquitto"] = new()
        {
            Enabled = visual, ClientUsername = "test", Users = [new() { Name = "test", Password = "test-secret" }],
            CustomUsername = "custom", CustomPassword = "custom-secret"
        } } };
        new MqttEnvironmentAdapter().Apply(copy, new Uri("mqtt://localhost:1234"), binding);
        copy["Username"]!.GetValue<string>().ShouldBe(visual ? "test" : "custom");
        copy["Password"]!.GetValue<string>().ShouldBe(visual ? "test-secret" : "custom-secret");
        copy["Address"]!.GetValue<string>().ShouldBe("mqtt://localhost:1234/");
        copy["ClientId"]!.GetValue<string>().ShouldBe("KeepCase");
        resource.ToJsonString().ShouldBe(original);
        new MqttEnvironmentAdapter().Apply(copy, new Uri("mqtt://localhost:1234"), binding with { Provider = "rabbitmq-mqtt" });
        copy.ContainsKey("Username").ShouldBeFalse();
        copy.ContainsKey("Password").ShouldBeFalse();
    }

    [Fact]
    public void Omitted_optional_settings_keep_their_defaults_when_read_from_JSON()
    {
        var profile = System.Text.Json.JsonSerializer.Deserialize("{}", EnvironmentJsonContext.Default.EnvironmentProfile)!;
        profile.StartupTimeoutSeconds.ShouldBe(60);
        profile.Resources.ShouldBeEmpty();
        profile = System.Text.Json.JsonSerializer.Deserialize("""{"Resources":{"Resources:broker":{"Provider":"mosquitto"}}}""", EnvironmentJsonContext.Default.EnvironmentProfile)!;
        var binding = profile.Resources["Resources:broker"];
        binding.PullIfMissing.ShouldBeTrue();
        binding.Mappings.ShouldBe("[]");
        binding.Files.ShouldBeEmpty();
        binding.Variables.ShouldBeEmpty();
    }
    private const string Configuration = """
        {"Resources":{"Broker":{"Type":"mqtt.client","Address":"mqtt://original:1883","ClientId":"KeepCase"}},
         "Workflows":{"main":{"Resources":{"Api":{"Type":"http.client","BaseAddress":"https://original/api/","Headers":{"CustomCase":"keep"}}}}}}
        """;

    [Fact]
    public async Task Bindings_are_applied_only_to_a_copy_and_owned_services_are_released_in_reverse_order()
    {
        var disposed = new List<string>();
        var mqtt = new FakeProvider("mqtt", "mqtt.client", new Uri("mqtt://localhost:1234"), disposed);
        var http = new FakeProvider("http", "http.client", new Uri("http://localhost:5678"), disposed);
        var preparation = Create(mqtt, http);
        var profile = Profile();
        await using (var result = await preparation.PrepareAsync(Configuration, profile))
        {
            result.Configuration["Resources"]!["Broker"]!["Address"]!.GetValue<string>().ShouldBe("mqtt://localhost:1234/");
            result.Configuration["Resources"]!["Broker"]!["ClientId"]!.GetValue<string>().ShouldBe("KeepCase");
            result.Configuration["Workflows"]!["main"]!["Resources"]!["Api"]!["BaseAddress"]!.GetValue<string>().ShouldBe("http://localhost:5678/api/");
            result.Configuration["Workflows"]!["main"]!["Resources"]!["Api"]!["Headers"]!["CustomCase"]!.GetValue<string>().ShouldBe("keep");
            profile.Resources["Resources:Broker"].Provider.ShouldBe("mqtt");
            JsonNode.Parse(Configuration)!["Resources"]!["Broker"]!["Address"]!.GetValue<string>().ShouldBe("mqtt://original:1883");
        }
        disposed.ShouldBe(["http", "mqtt"]);
    }

    [Fact]
    public async Task Failure_and_cancellation_roll_back_only_owned_services_and_preserve_original_exception()
    {
        var disposed = new List<string>();
        var mqtt = new FakeProvider("mqtt", "mqtt.client", new Uri("mqtt://localhost:1234"), disposed);
        var error = new IOException("startup failure");
        var http = new FakeProvider("http", "http.client", new Uri("http://localhost:5678"), disposed) { Failure = error };
        var actual = await Should.ThrowAsync<IOException>(() => Create(mqtt, http).PrepareAsync(Configuration, Profile()));
        actual.ShouldBeSameAs(error);
        disposed.ShouldBe(["mqtt"]);
        disposed.Clear();
        using var cancellation = new CancellationTokenSource();
        mqtt.OnStart = cancellation.Cancel;
        await Should.ThrowAsync<OperationCanceledException>(() => Create(mqtt, http).PrepareAsync(Configuration, Profile(), cancellation.Token));
        disposed.ShouldBe(["mqtt"]);
        http.Starts.ShouldBe(1);
    }

    [Fact]
    public async Task All_bindings_are_validated_before_start_and_existing_services_are_not_owned()
    {
        var disposed = new List<string>();
        var mqtt = new FakeProvider("mqtt", "mqtt.client", new Uri("mqtt://localhost:1234"), disposed);
        await Should.ThrowAsync<ArgumentException>(() => Create(mqtt).PrepareAsync(Configuration, Profile()));
        mqtt.Starts.ShouldBe(0);
        await using var existing = await Create(mqtt).PrepareAsync(Configuration, new());
        existing.Services.All(item => item.ContainerId is null && item.Provider == "existing").ShouldBeTrue();
        existing.Configuration.ToJsonString().ShouldBe(JsonNode.Parse(Configuration)!.ToJsonString());
        disposed.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{not-json}")]
    [InlineData("[{\"Request\":{},\"Response\":null}]")]
    public async Task Invalid_WireMock_customization_is_rejected_before_any_container_starts(string mappings)
    {
        var mqtt = new FakeProvider("mqtt", "mqtt.client", new Uri("mqtt://localhost:1234"), []);
        var profile = Profile();
        profile.Resources["Workflows:main:Resources:Api"] = new() { Provider = "wiremock", Mappings = mappings };
        await Should.ThrowAsync<Exception>(() => Create(mqtt, new ConfiguredContainerProvider(FluxFlow.Plugins.Http.WireMockContainer.Definition)).PrepareAsync(Configuration, profile));
        mqtt.Starts.ShouldBe(0);
    }

    [Fact]
    public async Task Cleanup_continues_after_a_failure_and_retries_only_unreleased_services()
    {
        var disposed = new List<string>();
        var failure = new IOException("cleanup unavailable");
        var mqtt = new FakeProvider("mqtt", "mqtt.client", new Uri("mqtt://localhost:1234"), disposed);
        var http = new FakeProvider("http", "http.client", new Uri("http://localhost:5678"), disposed) { CleanupFailure = failure };
        var environment = await Create(mqtt, http).PrepareAsync(Configuration, Profile());
        try
        {
            var error = await Should.ThrowAsync<AggregateException>(() => environment.DisposeAsync().AsTask());
            error.InnerExceptions.Single().ShouldBeSameAs(failure);
            disposed.ShouldBe(["http", "mqtt"]);
        }
        finally
        {
            http.CleanupFailure = null;
            await environment.DisposeAsync();
        }
        disposed.ShouldBe(["http", "mqtt", "http"]);
        await environment.DisposeAsync();
        disposed.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Locked_profile_overrides_fail_before_any_service_starts()
    {
        var mqtt = new FakeProvider("mqtt", "mqtt.client", new Uri("mqtt://localhost:1234"), []);
        var profile = Profile();
        var registry = new ImageRegistry();
        var preset = new ContainerProfile { Name = "P2", RegistryId = registry.Id, Provider = "wiremock", SetupLocked = true, Settings = new() { Image = "mock:1" } };
        profile.Resources["Workflows:main:Resources:Api"] = preset.CreateBinding([registry]) with { Mappings = """[{"Request":{},"Response":{}}]""" };
        var error = await Should.ThrowAsync<ArgumentException>(() => Create(mqtt, new ConfiguredContainerProvider(FluxFlow.Plugins.Http.WireMockContainer.Definition)).PrepareAsync(Configuration, profile));
        error.Message.ShouldContain("locked setup");
        mqtt.Starts.ShouldBe(0);
    }

    internal static EnvironmentPreparation Create(params IEnvironmentProvider[] providers) => new([new MqttEnvironmentAdapter(), new HttpEnvironmentAdapter()], providers);
    private static EnvironmentProfile Profile() => new() { Resources = new()
        { ["Resources:Broker"] = new() { Provider = "mqtt" }, ["Workflows:main:Resources:Api"] = new() { Provider = "http" } } };

    internal sealed class FakeProvider(string name, string type, Uri endpoint, List<string> disposed) : IEnvironmentProvider
    {
        public string Name => name;
        public string ResourceType => type;
        public Exception? Failure { get; set; }
        public Exception? CleanupFailure { get; set; }
        public Action? OnStart { get; set; }
        public int Starts { get; private set; }
        public void Validate(ResourceBinding binding) { }
        public Task<IEnvironmentService> StartAsync(ResourceBinding binding, CancellationToken cancellationToken)
        {
            Starts++;
            if (Failure is not null) throw Failure;
            OnStart?.Invoke();
            return Task.FromResult<IEnvironmentService>(new Lease(name, endpoint, disposed, () => CleanupFailure));
        }
    }
    private sealed class Lease(string name, Uri endpoint, List<string> disposed, Func<Exception?> cleanupFailure) : IEnvironmentService
    {
        public Uri Endpoint => endpoint;
        public string ContainerId => name;
        public ValueTask DisposeAsync()
        {
            disposed.Add(name);
            if (cleanupFailure() is { } error) throw error;
            return ValueTask.CompletedTask;
        }
    }
}
