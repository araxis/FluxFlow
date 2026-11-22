using FluxFlow.Environments;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ServiceBindingSelectionTests
{
    [Theory]
    [InlineData("source.example:5000/team/mock@sha256:abc", "team/mock@sha256:abc")]
    [InlineData("mirror.example:5100/team/mock:Stable", "team/mock:Stable")]
    [InlineData("team/mock", "team/mock")]
    public void Selecting_a_provider_uses_the_selected_registry_once(string declared, string relative)
    {
        var server = new EnvironmentProviderDescription("mock", "http.client", "Mock", declared);
        var defaults = new EnvironmentDefaults { Registries = [new() { Address = "mirror.example:5100" }] };
        var choices = new ServiceBindingSelection(server.ResourceType, defaults, [server]);
        choices.SelectProvider(new(), server.Name).Image.ShouldBe($"mirror.example:5100/{relative}");
    }

    [Fact]
    public void Run_using_requires_a_default_for_the_selected_server_and_Use_profile_copies_a_compatible_snapshot()
    {
        var first = new EnvironmentProviderDescription("first", "mqtt.client", "First", "first:4");
        var second = new EnvironmentProviderDescription("second", "mqtt.client", "Second", "second:2");
        var http = new EnvironmentProviderDescription("mock", "http.client", "Mock", "mock:1");
        var registry = new ImageRegistry { Address = "mirror.example:5100" };
        var preset = ContainerProfileServers.Create(first, registry) with { DefaultForResourceType = first.ResourceType };
        preset.Settings.Variables["Key"] = "value";
        var wrongType = ContainerProfileServers.Create(http, registry);
        var unavailable = preset with { Id = "unavailable", Provider = "removed" };
        var defaults = new EnvironmentDefaults { Profiles = [preset, wrongType, unavailable], Registries = [registry] };
        var choices = new ServiceBindingSelection(first.ResourceType, defaults, [first, second, http]);
        var manual = new ResourceBinding();
        choices.Profiles.ShouldBe(new[] { preset });
        choices.SelectProfile(manual, wrongType.Id).ShouldBeSameAs(manual);
        choices.SelectProfile(manual, unavailable.Id).ShouldBeSameAs(manual);
        var stock = choices.SelectProvider(manual, second.Name);
        stock.Image.ShouldBe("mirror.example:5100/second:2");
        stock.Profile.ShouldBeNull();
        var copied = choices.SelectProfile(stock, preset.Id);
        copied.Provider.ShouldBe(first.Name);
        copied.Profile!.Id.ShouldBe(preset.Id);
        choices.SelectProvider(copied, first.Name).ShouldBeSameAs(copied);
        choices.SelectProfile(copied, preset.Id).ShouldBeSameAs(copied);
        copied.Variables["Key"] = "override";
        copied.Profile.Settings.Variables["Key"].ShouldBe("value");
        var reapplied = choices.SelectProfile(copied, preset.Id);
        reapplied.Variables["Key"].ShouldBe("value");
        copied.Variables["Key"].ShouldBe("override");
    }
}
