using FluxFlow.Environments;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Settings;
using FluxFlow.Studio.UI.Features.Environments;
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
    public void Service_variable_defaults_do_not_materialize_or_overwrite_saved_values()
    {
        var field = new ServiceVariable("VHOST", "Virtual host", "Virtual host", "/");
        var original = new ResourceBinding();
        field.Read(original).ShouldBe("/");
        field.Write(original, " ").ShouldBeSameAs(original);
        var saved = field.Write(original, "/");
        saved.Variables["VHOST"].ShouldBe("/");
        original.Variables.ShouldBeEmpty();
        field.Write(saved, " / ").ShouldBeSameAs(saved);
        var cleared = field.Write(saved, null);
        cleared.Variables.ShouldBeEmpty();
        field.Read(cleared).ShouldBe("/");
    }

    [Theory]
    [InlineData("Username", "NATS_USERNAME_ENVIRONMENT_VARIABLE", false)]
    [InlineData("Password", "NATS_PASSWORD_ENVIRONMENT_VARIABLE", false)]
    [InlineData("Token", "NATS_TOKEN_ENVIRONMENT_VARIABLE", false)]
    [InlineData("Token", "NATS_TOKEN_ENVIRONMENT_VARIABLE", true)]
    public async Task Nats_access_field_edits_preserve_custom_config_snapshots_and_history(string label, string key, bool locked)
    {
        const string path = "Resources:broker";
        const string file = "/etc/nats/nats.conf";
        const string custom = "# managed separately\r\nport: 4333\ninclude local.conf";
        var binding = new ResourceBinding { Provider = "nats", Files = new() { [file] = custom },
            Variables = new() { [key] = "ORIGINAL_REF", ["OTHER"] = " unchanged " } };
        if (locked) binding = binding with { Profile = new("snapshot", "NATS", true, binding.CopySetup()) };
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Resources":{"broker":{"Type":"nats.connection"}},"Workflows":{"main":{}}}""");
        graph.SetEnvironment(new() { Resources = new() { [path] = binding } });
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        var configuration = graph.ExportConfiguration();
        ResourceBinding Current() => graph.GetEnvironment().Resources[path];

        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ServiceSettingsEditor>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ServiceSettingsEditor.ResourcePath)] = path }));
            var field = components.Items.OfType<MudTextField<string>>().Single(item =>
                Equals(item.UserAttributes.GetValueOrDefault("aria-label"), label + " environment variable"));
            field.Value.ShouldBe("ORIGINAL_REF");
            field.ReadOnly.ShouldBe(locked);
            var originalEdit = field.ValueChanged;
            await originalEdit.InvokeAsync("  NEW_REF  ");
            if (locked)
            {
                await field.ValueChanged.InvokeAsync("");
                Current().ValidateSnapshot();
                graph.ToProjectJson().ShouldBe(before);
                graph.CanUndo.ShouldBeFalse();
                return;
            }
            Current().Variables[key].ShouldBe("NEW_REF");
            var edited = graph.ToProjectJson();
            await field.ValueChanged.InvokeAsync(" NEW_REF ");
            await field.ValueChanged.InvokeAsync("  ");
            Current().Variables.ContainsKey(key).ShouldBeFalse();
            Current().Variables["OTHER"].ShouldBe(" unchanged ");
            Current().Files[file].ShouldBe(custom);
            var cleared = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(edited);
            graph.Undo(); graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse();
            // A late commit from the original field must not reapply an undone edit or discard Redo.
            await originalEdit.InvokeAsync("NEW_REF");
            graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.Redo(); graph.ToProjectJson().ShouldBe(cleared);
            graph.ExportConfiguration().ShouldBe(configuration);
            var reopened = StudioTestServices.CreateGraph(); reopened.LoadProject(cleared);
            reopened.ToProjectJson().ShouldBe(cleared);
        });
    }

    [Fact]
    public async Task Nats_access_field_from_previous_profile_provider_cannot_write_a_reference()
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var preferences = scope.ServiceProvider.GetRequiredService<StudioPreferences>();
        var registry = new ImageRegistry();
        var profile = new ContainerProfile { Provider = "nats", RegistryId = registry.Id,
            Settings = new() { Provider = "nats", Image = "nats:latest" } };
        await preferences.SaveAsync(preferences.Value with { Environments = new() { Registries = [registry], Profiles = [profile] } });
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            await renderer.RenderComponentAsync<ResourceDefaultsSettings>();
            var lateEdit = components.Items.OfType<MudTextField<string>>().Single(item =>
                Equals(item.UserAttributes.GetValueOrDefault("aria-label"), "Token environment variable")).ValueChanged;
            await components.Items.OfType<MudSelect<string>>().Single(item => item.Label == "Connection type")
                .ValueChanged.InvokeAsync("http.client");
            await lateEdit.InvokeAsync("WRONG_PROVIDER_REF");
            var current = preferences.Value.Environments.Profiles.Single();
            current.Provider.ShouldBe("wiremock");
            current.Settings.Variables.ShouldBeEmpty();
        });
    }
}
