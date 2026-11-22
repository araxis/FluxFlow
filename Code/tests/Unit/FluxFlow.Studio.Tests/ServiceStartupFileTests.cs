using FluxFlow.Environments;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ServiceStartupFileTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\uFEFFfirst\r\nsecond\n\t\r\n")]
    public void Import_pins_even_the_exact_default_until_reset(string text)
    {
        var file = new ServiceStartupFile("Config", "/service.conf", "", b => b.Variables["default"]);
        var inherited = new ResourceBinding { Variables = new() { ["default"] = text }, Files = new() { ["/other"] = "keep" } };
        file.Write(inherited, text).ShouldBeSameAs(inherited);
        file.Reset(inherited).ShouldBeSameAs(inherited);
        var pinned = file.Write(inherited, text, import: true);
        pinned.Files[file.Path].ShouldBe(text);
        file.HasOverride(inherited).ShouldBeFalse();
        file.Write(pinned, text, import: true).ShouldBeSameAs(pinned);

        var changedDefault = pinned with { Variables = new() { ["default"] = "updated" } };
        file.Read(changedDefault).ShouldBe(text);
        var reset = file.Reset(changedDefault);
        file.Read(reset).ShouldBe("updated");
        reset.Files.Keys.ShouldBe(new[] { "/other" });
        changedDefault.Files[file.Path].ShouldBe(text);
    }

    [Fact]
    public void Generated_protection_uses_the_edited_provider_and_locked_snapshots_allow_no_changes()
    {
        var file = new ServiceStartupFile("Config", "/service.conf", "", _ => "", GeneratedWithUsers: true);
        var binding = new ResourceBinding
        {
            Provider = "other", BrokerAccess = new() { ["selected"] = new() { Enabled = true } },
            Files = new() { [file.Path] = "saved" }
        };
        file.Write(binding, "edited", provider: "selected").ShouldBeSameAs(binding);
        file.Write(binding, "imported", import: true, provider: "selected").ShouldBeSameAs(binding);
        file.Write(binding, "edited").Files[file.Path].ShouldBe("edited");
        file.HasOverride(file.Reset(binding)).ShouldBeFalse();

        var locked = binding with { Profile = new("preset", "Preset", true, binding.CopySetup()) };
        file.Write(locked, "edited").ShouldBeSameAs(locked);
        file.Write(locked, "imported", import: true).ShouldBeSameAs(locked);
        file.Reset(locked).ShouldBeSameAs(locked);
        file.Read(locked).ShouldBe("saved");
    }
}
