using System.Net;
using System.Text.Json;
using FluxFlow.Execution;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class EnvironmentDefaultsTests
{
    [Fact]
    public void Host_update_rejects_duplicate_renames_without_changing_existing_names()
    {
        var defaults = new EnvironmentDefaults().AddHost("Office").AddHost("Lab");
        var office = defaults.Infrastructure[0];
        var draft = office.CreateTopology();
        draft.Nodes[0].Name = " LAB ";
        Should.Throw<ArgumentException>(() => defaults.UpdateHost(office, draft));
        office.Name.ShouldBe("Office");
        office.Topology.ShouldBeNull();

        // Older definitions may already share a name. A layout edit must keep working.
        var legacy = defaults with { Infrastructure = [office, defaults.Infrastructure[1] with { Name = " office " }] };
        draft.Nodes[0].Name = office.Name;
        draft.Nodes[0].X = 75;
        var moved = legacy.UpdateHost(office, draft);
        moved.Infrastructure[0].Topology!.Nodes[0].X.ShouldBe(75);
        moved.Infrastructure[1].Name.ShouldBe(" office ");
    }

    [Fact]
    public void Host_update_validates_and_copies_the_edited_topology_without_mutating_legacy_settings()
    {
        var host = new InfrastructureHost { Name = "Office", Description = "  saved description  ",
            Services = [new() { Name = "Broker", Endpoint = "mqtt://office:1883" }] };
        var defaults = new EnvironmentDefaults { Infrastructure = [host, new()] }.AddRegistry();
        var draft = host.CreateTopology();
        var placed = host.CreateTopology();
        draft.Nodes[0].Name = "Lab";
        draft.Nodes[1].X = 160;
        Should.Throw<ArgumentException>(() => defaults.UpdateHost(host, new()));
        var updated = defaults.UpdateHost(host, draft);
        draft.Nodes.Clear();
        var saved = updated.Infrastructure[0];
        saved.Id.ShouldBe(host.Id);
        saved.Name.ShouldBe("Lab");
        saved.Description.ShouldBe(host.Description);
        saved.Services.ShouldBeEmpty();
        saved.Topology!.Nodes[1].X.ShouldBe(160);
        saved.Topology.Nodes[1].ExistingService.ShouldBe(host.Services.Single());
        host.Topology.ShouldBeNull();
        placed.Nodes[0].Name.ShouldBe("Office");
        updated.Infrastructure[1].ShouldBeSameAs(defaults.Infrastructure[1]);
        updated.Registries.ShouldBeSameAs(defaults.Registries);
        updated.UpdateHost(host, draft).ShouldBeSameAs(updated);
        var removed = updated.RemoveHost(saved);
        removed.UpdateHost(saved, draft).ShouldBeSameAs(removed);
    }

    [Fact]
    public void Host_list_edits_preserve_existing_names_and_ignore_replaced_or_removed_entries()
    {
        var office = new InfrastructureHost { Name = " Office " };
        var defaults = new EnvironmentDefaults { Infrastructure = [office] }.AddRegistry()
            .AddProfile(new("wiremock", "http.client", "WireMock", "mock:1"));
        Should.Throw<ArgumentException>(() => defaults.AddHost(" office"));
        Should.Throw<ArgumentException>(() => defaults.AddHost("  "));
        var added = defaults.AddHost(" Lab ");
        added.Infrastructure[0].ShouldBeSameAs(office);
        added.Infrastructure[0].Name.ShouldBe(" Office ");
        added.Infrastructure[1].Name.ShouldBe("Lab");
        defaults.Infrastructure.Count.ShouldBe(1);

        var reloaded = added with { Infrastructure = [office with { }, added.Infrastructure[1]] };
        reloaded.RemoveHost(office).ShouldBeSameAs(reloaded);
        var removed = reloaded.RemoveHost(reloaded.Infrastructure[0]);
        removed.Infrastructure.Single().ShouldBeSameAs(added.Infrastructure[1]);
        removed.RemoveHost(reloaded.Infrastructure[0]).ShouldBeSameAs(removed);
        removed.Registries.ShouldBeSameAs(defaults.Registries);
        removed.Profiles.ShouldBeSameAs(defaults.Profiles);
    }

    [Theory]
    [InlineData("http.client", "mqtt.client", null)]
    [InlineData("mqtt.client", null, "mqtt.client")]
    public void Profile_server_change_uses_the_target_type_to_preserve_or_release_the_default(
        string targetType, string? draftAssignment, string? expectedAssignment)
    {
        var mqtt = new EnvironmentProviderDescription("mosquitto", "mqtt.client", "Mosquitto", "mosquitto:2");
        var target = new EnvironmentProviderDescription("target", targetType, "Target", "target:1");
        var defaults = new EnvironmentDefaults().AddRegistry().AddProfile(mqtt);
        defaults = defaults.SetDefault(defaults.Profiles.Single().Id, mqtt);
        var original = defaults.Profiles.Single();
        var draft = original with { Provider = target.Name, Settings = original.Settings with { Provider = target.Name },
            DefaultForResourceType = draftAssignment };
        defaults.UpdateProfile(original, draft, [mqtt]).ShouldBeSameAs(defaults);
        var updated = defaults.UpdateProfile(original, draft, [mqtt, target]);
        updated.Profiles.Single().Provider.ShouldBe(target.Name);
        updated.Profiles.Single().DefaultForResourceType.ShouldBe(expectedAssignment);
    }

    [Fact]
    public void Profile_update_requires_the_saved_original_and_preserves_assignment_and_snapshots()
    {
        var defaults = new EnvironmentDefaults().AddRegistry().AddProfile(new("wiremock", "http.client", "WireMock", "mock:1"));
        defaults = defaults.SetDefault(defaults.Profiles.Single().Id, new("wiremock", "http.client", "WireMock", "mock:1"));
        var original = defaults.Profiles.Single();
        defaults = defaults.UpdateProfile(original, original with { SetupLocked = true });
        var locked = defaults.Profiles.Single();
        var snapshot = locked.CreateBinding(defaults.Registries);
        var unchanged = locked with { Settings = locked.Settings with { Variables = new(locked.Settings.Variables) } };
        defaults.UpdateProfile(locked, unchanged).ShouldBeSameAs(defaults);
        defaults.UpdateProfile(original, original with { Name = "Stale" }).ShouldBeSameAs(defaults);
        Should.Throw<ArgumentException>(() => defaults.UpdateProfile(locked, locked with { RegistryId = "removed" }));

        var updated = defaults.UpdateProfile(locked, locked with { Name = "Mirror", SetupLocked = false, DefaultForResourceType = null,
            Settings = locked.Settings with { Image = "custom:2" } });
        var profile = updated.Profiles.Single();
        profile.DefaultForResourceType.ShouldBe("http.client");
        var next = profile.CreateBinding(updated.Registries);
        next.Profile!.Name.ShouldBe("Mirror");
        next.Profile.SetupLocked.ShouldBeFalse();
        next.Image.ShouldBe("docker.io/custom:2");
        snapshot.Profile!.Name.ShouldBe(locked.Name);
        snapshot.Profile.SetupLocked.ShouldBeTrue();
        snapshot.Image.ShouldBe("docker.io/mock:latest");
    }

    [Fact]
    public void Registry_update_keeps_profile_links_and_only_new_bindings_use_the_new_host()
    {
        var registry = new ImageRegistry { Address = "old.example" };
        var profile = new ContainerProfile { Provider = "wiremock", RegistryId = registry.Id, SetupLocked = true,
            Settings = new() { Image = "team/server:2" } };
        var defaults = new EnvironmentDefaults { Registries = [new(), registry], Profiles = [profile] };
        var applied = profile.CreateBinding(defaults.Registries);
        var snapshot = JsonSerializer.Serialize(applied);
        defaults.UpdateRegistry(registry with { }).ShouldBeSameAs(defaults);
        defaults.UpdateRegistry(registry with { Id = "removed" }).ShouldBeSameAs(defaults);
        Should.Throw<ArgumentException>(() => defaults.UpdateRegistry(registry with { Address = "https://invalid.example" }));

        var edit = registry with { Name = "Private", Address = "new.example:5000", Description = "  mirror  ", Authentication = "Anonymous" };
        var updated = defaults.UpdateRegistry(edit);
        updated.Registries[0].ShouldBeSameAs(defaults.Registries[0]);
        updated.Registries[1].ShouldBe(edit);
        defaults.Registries[1].ShouldBeSameAs(registry);
        updated.Profiles.ShouldBeSameAs(defaults.Profiles);
        profile.CreateBinding(updated.Registries).Image.ShouldBe("new.example:5000/team/server:2");
        applied.Image.ShouldBe("old.example/team/server:2");
        applied.Profile!.SetupLocked.ShouldBeTrue();
        JsonSerializer.Serialize(applied).ShouldBe(snapshot);
    }

    [Fact]
    public void Registry_removal_uses_profile_references_by_id_even_for_identical_registry_addresses()
    {
        var original = new EnvironmentDefaults().AddRegistry();
        var used = original.Registries.Single();
        var defaults = original.AddProfile(new("mosquitto", "mqtt.client", "Mosquitto", "broker:1")).AddRegistry();
        var unused = defaults.Registries.Last();
        unused.Address.ShouldBe(used.Address);
        Should.Throw<InvalidOperationException>(() => defaults.RemoveRegistry(used.Id));
        var remaining = defaults.RemoveRegistry(unused.Id);
        remaining.Registries.Single().ShouldBeSameAs(used);
        remaining.Profiles.ShouldBeSameAs(defaults.Profiles);
        remaining.RemoveRegistry(unused.Id).ShouldBeSameAs(remaining);
        defaults.Registries.Count.ShouldBe(2);
    }

    [Fact]
    public void Adding_profiles_uses_the_first_registry_and_gives_each_profile_its_own_identity()
    {
        var mqtt = new EnvironmentProviderDescription("mosquitto", "mqtt.client", "Mosquitto", "eclipse-mosquitto:2.0.22");
        var empty = new EnvironmentDefaults();
        empty.AddProfile(mqtt).ShouldBeSameAs(empty);
        var original = empty with { Registries = [new(), new()] };
        var once = original.AddProfile(mqtt);
        var twice = once.AddProfile(mqtt);
        var first = once.Profiles.Single();
        var second = twice.Profiles.Last();
        original.Profiles.ShouldBeEmpty();
        second.Id.ShouldNotBe(first.Id);
        second.RegistryId.ShouldBe(original.Registries[0].Id);
        second.Settings.Image.ShouldBe("eclipse-mosquitto:latest");
        twice.RemoveProfile(first.Id).Profiles.Single().ShouldBeSameAs(second);
    }

    [Fact]
    public void Default_edits_transfer_only_the_matching_type_and_ignore_inapplicable_actions()
    {
        var mqtt = new EnvironmentProviderDescription("broker", "mqtt.client", "Broker", "broker:1");
        var http = new EnvironmentProviderDescription("server", "http.client", "Server", "server:1");
        var first = new ContainerProfile { Provider = mqtt.Name, DefaultForResourceType = mqtt.ResourceType };
        var second = new ContainerProfile { Provider = mqtt.Name, SetupLocked = true };
        var other = new ContainerProfile { Provider = http.Name, DefaultForResourceType = http.ResourceType };
        var original = new EnvironmentDefaults { Profiles = [first, second, other] };

        var assigned = original.SetDefault(second.Id, mqtt);
        assigned.Profiles[0].ShouldBe(first with { DefaultForResourceType = null });
        assigned.Profiles[1].ShouldBe(second with { DefaultForResourceType = mqtt.ResourceType });
        assigned.Profiles[2].ShouldBeSameAs(other);
        assigned.Profiles[1].Settings.ShouldBeSameAs(second.Settings);
        original.Profiles[0].DefaultForResourceType.ShouldBe(mqtt.ResourceType);

        assigned.SetDefault(second.Id, mqtt).ShouldBeSameAs(assigned);
        assigned.SetDefault("removed", mqtt).ShouldBeSameAs(assigned);
        assigned.SetDefault(first.Id, http).ShouldBeSameAs(assigned);
        assigned.ClearDefault(first.Id, mqtt).ShouldBeSameAs(assigned);
        assigned.ClearDefault(second.Id, http).ShouldBeSameAs(assigned);
        var cleared = assigned.ClearDefault(second.Id, mqtt);
        cleared.Profiles[1].ShouldBe(second);
        cleared.Profiles[2].ShouldBeSameAs(other);
        cleared.ClearDefault(second.Id, mqtt).ShouldBeSameAs(cleared);
    }

    [Theory]
    [InlineData(null, "team/mock:1")]
    [InlineData("missing", "team/mock:1")]
    [InlineData("hub", "private.example/team/mock:1")]
    [InlineData("hub", "team/mock:1:2")]
    [InlineData("hub", "team/mock:")]
    public void Profiles_require_a_registered_host_and_separate_image_name(string? registryId, string image)
    {
        var profile = new ContainerProfile { RegistryId = registryId, Settings = new() { Image = image } };
        var defaults = new EnvironmentDefaults { Registries = [new() { Id = "hub", Name = "Docker Hub" }], Profiles = [profile] };
        Should.Throw<ArgumentException>(defaults.Validate);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("provider")]
    [InlineData("mappings")]
    [InlineData("files")]
    [InlineData("variables")]
    [InlineData("pull")]
    public void Locked_snapshots_reject_setup_overrides_at_validation_and_provider_boundaries(string field)
    {
        var registry = new ImageRegistry();
        var profile = new ContainerProfile { Name = "P2", RegistryId = registry.Id, Provider = "wiremock", SetupLocked = true,
            Settings = new() { Image = "mock:1", Files = new() { ["/setup.txt"] = "Original" } } };
        var environment = new EnvironmentProfile { Resources = new() { ["Resources:api"] = profile.CreateBinding([registry]) } }.Copy();
        environment.Validate();
        var binding = environment.Resources["Resources:api"];
        switch (field)
        {
            case "image": binding.Image = "mock:2"; break;
            case "provider": binding.Provider = "existing"; break;
            case "mappings": binding.Mappings = """[{"Request":{},"Response":{}}]"""; break;
            case "files": binding.Files["/setup.txt"] = "Changed"; break;
            case "variables": binding.Variables["Custom"] = "Changed"; break;
            case "pull": binding.PullIfMissing = false; break;
        }
        Should.Throw<ArgumentException>(environment.Validate).Message.ShouldContain("locked setup");
        Should.Throw<ArgumentException>(() => new Testcontainers.ConfiguredContainerProvider(FluxFlow.Plugins.Http.WireMockContainer.Definition).Validate(binding)).Message.ShouldContain("locked setup");
        profile.Settings.Files["/setup.txt"].ShouldBe("Original");
        binding.Profile!.Settings.Files["/setup.txt"].ShouldBe("Original");
    }

    [Fact]
    public void Saved_profile_snapshots_survive_profile_and_registry_changes_and_removal()
    {
        var registry = new ImageRegistry { Id = "private", Address = "registry.example" };
        var profile = new ContainerProfile { Name = "P2", Provider = "wiremock", RegistryId = registry.Id, SetupLocked = true,
            Settings = new() { Image = "mock:1", Mappings = """[{"Request":{"Path":"/P2"},"Response":{"Body":"KeepCase"}}]""", Variables = new() { ["Case"] = "Original" } } };
        var defaults = new EnvironmentDefaults { Registries = [registry], Profiles = [profile] };
        var applied = new EnvironmentProfile { Resources = new() { ["Resources:api"] = profile.CreateBinding(defaults.Registries) } };
        profile.Settings.Variables["Case"] = "Later";
        defaults = defaults.RemoveProfile(profile.Id);
        defaults.RemoveProfile(profile.Id).ShouldBeSameAs(defaults);
        defaults = defaults with { Registries = [] };
        var reopened = applied.Copy();
        reopened.Validate();
        var binding = reopened.Resources["Resources:api"];
        binding.Image.ShouldBe("registry.example/mock:1");
        binding.Profile!.Name.ShouldBe("P2");
        binding.Profile.SetupLocked.ShouldBeTrue();
        binding.Profile.Settings.Mappings.ShouldBe(profile.Settings.Mappings);
        binding.Variables["Case"].ShouldBe("Original");
        binding.Profile.Settings.Variables["Case"].ShouldBe("Original");
        binding.Profile.Settings.Variables["Case"] = "Detached copy";
        applied.Resources["Resources:api"].Profile!.Settings.Variables["Case"].ShouldBe("Original");
    }

    [Fact]
    public void Profiles_do_not_require_a_resource_mapping_and_applied_settings_are_independent()
    {
        var registry = new ImageRegistry { Id = "company", Name = "Company", Address = "registry.example:5000" };
        var profile = new ContainerProfile { Name = "Mock", RegistryId = registry.Id, Provider = "wiremock", Settings = new()
            { Image = "team/mock:1", PullIfMissing = false, Mappings = "[]", Files = new() { ["/app/setup.txt"] = "KeepCase" }, Variables = new() { ["Custom"] = "Value" } } };
        var imageOnly = new ContainerProfile { Name = "Infrastructure", RegistryId = registry.Id, Settings = new() { Image = "infrastructure/helper:1" } };
        var defaults = new EnvironmentDefaults { Registries = [registry], Profiles = [profile, imageOnly] };
        defaults.Validate();
        imageOnly.ResolveImage(defaults.Registries).ShouldBe("registry.example:5000/infrastructure/helper:1");
        Should.Throw<InvalidOperationException>(() => imageOnly.CreateBinding(defaults.Registries));
        var binding = profile.CreateBinding(defaults.Registries);
        binding.Provider.ShouldBe("wiremock");
        binding.Image.ShouldBe("registry.example:5000/team/mock:1");
        binding.PullIfMissing.ShouldBeFalse();
        binding.Files["/app/setup.txt"] = "Project override";
        profile.Settings.Files["/app/setup.txt"].ShouldBe("KeepCase");
        profile.Settings.Variables["Custom"] = "Default changed";
        binding.Variables["Custom"].ShouldBe("Value");
        var saved = JsonSerializer.Serialize(new EnvironmentProfile { Resources = new() { ["Resources:api"] = binding } }, EnvironmentJsonContext.Default.EnvironmentProfile);
        saved.ShouldNotContain("RegistryId");
        saved.ShouldNotContain("Authentication");
        saved.ShouldNotContain("Password");
    }

    [Theory]
    [InlineData("https://registry.example")]
    [InlineData("user:password@registry.example")]
    [InlineData("registry.example/team")]
    [InlineData("-registry.example")]
    public void Invalid_registry_addresses_are_rejected(string address)
    {
        Should.Throw<ArgumentException>(() => new ImageRegistry { Address = address }.Validate());
    }

    [Fact]
    public void Defaults_reject_missing_registries_and_duplicate_type_assignments()
    {
        var registry = new ImageRegistry();
        var defaults = new EnvironmentDefaults { Registries = [registry], Profiles = [
            new() { RegistryId = registry.Id, Provider = "mosquitto", DefaultForResourceType = "mqtt.client", Settings = new() { Image = "broker:1" } }
        ] };
        Should.Throw<ArgumentException>(() => (defaults with { Profiles = [defaults.Profiles[0], defaults.Profiles[0] with { Id = "second" }] }).Validate());
        Should.Throw<ArgumentException>(() => (defaults with { Profiles = [defaults.Profiles[0] with { RegistryId = "missing" }] }).Validate());
    }

    [Fact]
    public async Task Sign_in_without_a_credential_helper_does_not_write_passwords_to_disk()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new RegistryHandler());
        var access = new DockerRegistryAccess(client, directory);
        var result = await access.SignInAsync(new(new() { Address = "registry.example" }, "user", "example-token"), CancellationToken.None);
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("credential helper");
        Directory.Exists(directory).ShouldBeFalse();
    }

    [Fact]
    public async Task Registry_probe_reports_authentication_requirements_without_claiming_a_login()
    {
        var handler = new RegistryHandler();
        using var client = new HttpClient(handler);
        var result = await new DockerRegistryAccess(client).TestAsync(new() { Address = "docker.io" }, CancellationToken.None);
        handler.Address.ShouldBe("https://registry-1.docker.io/v2/");
        result.Success.ShouldBeTrue();
        result.Message.ShouldContain("does not validate saved credentials");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => new DockerRegistryAccess(client).TestAsync(new(), cancelled.Token));
    }

    private sealed class RegistryHandler : HttpMessageHandler
    {
        public string? Address { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Address = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }
}
