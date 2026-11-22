using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using FluxFlow.Studio.UI.Features.Environments.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Fact]
    public void Portable_image_edits_preserve_registry_identity_and_digest()
    {
        var registry = new ImageRegistry { Id = "mirror", Address = "mirror.example:5000" };
        var profile = new ContainerProfile { Provider = "nats", RegistryId = "removed", SetupLocked = true,
            Settings = new() { Image = "team/image@sha256:abc" } };
        var missing = new ContainerImageEdits(profile, [registry]);
        missing.SetName("other").ShouldBeSameAs(profile);
        missing.SelectRegistry("unknown").ShouldBeSameAs(profile);
        var policy = missing.SetPullIfMissing(false);
        policy.RegistryId.ShouldBe("removed");
        policy.Settings.Image.ShouldBe(profile.Settings.Image);
        var selected = missing.SelectRegistry(registry.Id);
        selected.Settings.Image.ShouldBe(profile.Settings.Image);
        var renamed = new ContainerImageEdits(selected, [registry]).SetName(" team/renamed ");
        renamed.ResolveImage([registry]).ShouldBe("mirror.example:5000/team/renamed@sha256:abc");
        // Only project snapshots are locked; the reusable profile remains editable.
        var binding = renamed.CreateBinding([registry]);
        var snapshot = new ContainerProfile { Settings = binding };
        var mirror = new ImageRegistry { Id = "other", Address = "other.example" };
        var locked = new ContainerImageEdits(snapshot, [registry, mirror], copyRegistryAddress: true);
        locked.SetVersion("sha256:def").ShouldBeSameAs(snapshot);
        locked.SetPullIfMissing(false).ShouldBeSameAs(snapshot);
        locked.SelectRegistry(mirror.Id).ShouldBeSameAs(snapshot);
    }

    [Theory]
    [InlineData("mirror.example:5000/team/nats", true)]
    [InlineData("MIRROR.example:5000/team/nats:Stable", true)]
    [InlineData("retired.example/team/nats@sha256:abc", false)]
    [InlineData("mirror.example:5001/team/nats@sha256:abc", false)]
    [InlineData(null, false)]
    [InlineData("mirror.example:5000/team/nats", true, true)]
    public async Task Project_image_edits_preserve_exact_pull_policy_target_and_history(string? image, bool registryKnown, bool locked = false)
    {
        const string path = "Resources:broker";
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new ImageRegistry { Id = "mirror", Address = "mirror.example:5000" };
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry] } });
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"nats.connection"}},"Workflows":{"main":{}}}""");
        var initial = new ResourceBinding { Provider = "nats", Image = image };
        if (locked) initial = initial with { Profile = new("profile", "Locked NATS", true, initial.CopySetup()) };
        graph.SetEnvironment(new() { Resources = new() { [path] = initial } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ServiceSettingsEditor>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ServiceSettingsEditor.ResourcePath)] = path }));
            var name = components.Items.OfType<MudTextField<string>>().Single(item =>
                Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Image name"));
            name.Disabled.ShouldBe(locked || !registryKnown);
            if (image is null) name.Value.ShouldBe("nats");
            var pull = components.Items.OfType<MudCheckBox<bool>>().Single(item =>
                Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Pull image if missing"));
            var originalPull = pull.ValueChanged;
            pull.Disabled.ShouldBe(locked);
            await originalPull.InvokeAsync(true);
            graph.CanUndo.ShouldBeFalse();
            await pull.ValueChanged.InvokeAsync(false);
            if (locked)
            {
                await name.ValueChanged.InvokeAsync("changed");
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                return;
            }
            var binding = graph.GetEnvironment().Resources[path];
            binding.PullIfMissing.ShouldBeFalse();
            binding.Image.ShouldBe(image);
            var policy = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            await originalPull.InvokeAsync(false);
            graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(policy);
            var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(policy);
            reopened.ToProjectJson().ShouldBe(policy);

            if (!registryKnown)
            {
                await name.ValueChanged.InvokeAsync("wrong");
                graph.ToProjectJson().ShouldBe(policy);
                var selector = components.Items.OfType<MudSelect<string>>().Single(item =>
                    Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Registry"));
                await selector.ValueChanged.InvokeAsync(registry.Id);
                graph.GetEnvironment().Resources[path].Image.ShouldBe(image is null
                    ? "mirror.example:5000/nats:latest" : "mirror.example:5000/team/nats@sha256:abc");
                var switched = graph.ToProjectJson();
                graph.Undo(); graph.ToProjectJson().ShouldBe(policy);
                graph.Redo(); graph.ToProjectJson().ShouldBe(switched);
                reopened.LoadProject(switched); reopened.ToProjectJson().ShouldBe(switched);
            }
        });
    }
}
