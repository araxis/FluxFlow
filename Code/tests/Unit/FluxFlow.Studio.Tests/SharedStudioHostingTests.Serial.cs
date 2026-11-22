using FluxFlow.Studio.UI.Features.Connections;
using FluxFlow.Studio.UI.Features.Designer;
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
    private sealed class PendingSerialDiscovery
    {
        public TaskCompletionSource<string[]> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<string[]> ListAsync(CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            return Response.Task;
        }
    }

    [Theory]
    [InlineData("select")]
    [InlineData("manual")]
    [InlineData("unavailable")]
    [InlineData("denied")]
    [InlineData("cancel")]
    public async Task Serial_connection_discovery_never_overwrites_manual_or_cancelled_selection(string action)
    {
        var services = CreateServices();
        var components = new CapturingComponents();
        var discovery = new PendingSerialDiscovery();
        services.AddSingleton<IComponentActivator>(components);
        services.AddKeyedSingleton<Func<CancellationToken, Task<string[]>>>(new FluxFlow.Plugins.SuggestionKey("FluxFlow.Plugins.Serial", "ports"), discovery.ListAsync);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("""{"Workflows":{"main":{"Resources":{"wire":{"Type":"serial.port","portName":"remote-port","Parity":2,"Extra":{"Keep":42}}}}}}""");
        graph.ResetHistory();
        var before = graph.ToProjectJson();
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<MudPopoverProvider>();
            var view = await renderer.RenderComponentAsync<ConnectionInspector>(ParameterView.FromDictionary(new Dictionary<string, object?>
                { ["Selection"] = graph.Connections.Single().Path }));
            var picker = components.Items.OfType<MudAutocomplete<string>>().Single();
            var listing = picker.SearchFunc!("", CancellationToken.None)!;
            graph.ToProjectJson().ShouldBe(before);
            if (action is "unavailable" or "denied")
            {
                Exception failure = action == "denied" ? new UnauthorizedAccessException("Port access denied") : new IOException("Port list unavailable");
                discovery.Response.SetException(failure);
                (await listing).ShouldBeEmpty();
                // A failed lookup must still offer the exact manually entered name.
                (await picker.SearchFunc!("/dev/remote", CancellationToken.None)!).ShouldBe(["/dev/remote"]);
            }
            if (action != "select")
            {
                await picker.ValueChanged.InvokeAsync("/dev/remote");
                graph.ToProjectJson().ShouldBe(before);
                if (action == "cancel") await picker.OnKeyDown.InvokeAsync(new() { Key = "Escape" });
                await picker.OpenChanged.InvokeAsync(false);
                discovery.Token.IsCancellationRequested.ShouldBeTrue();
            }
            discovery.Response.TrySetResult(["COM8"]);
            var ports = await listing;
            if (action == "select")
            {
                (await picker.SearchFunc!("remote-port", CancellationToken.None)!).ShouldBe(["COM8"]);
                await picker.ValueChanged.InvokeAsync("COM");
                graph.ToProjectJson().ShouldBe(before);
                await picker.ValueChanged.InvokeAsync(ports.Single());
                await picker.OpenChanged.InvokeAsync(false);
            }
            view.ToHtmlString().ShouldNotContain("aria-label=\"Available ports\"");
            if (action == "cancel") { graph.ToProjectJson().ShouldBe(before); graph.CanUndo.ShouldBeFalse(); return; }
            var expected = action == "select" ? "COM8" : "/dev/remote";
            var settings = graph.Connections.Single().Settings;
            settings["portName"]!.GetValue<string>().ShouldBe(expected);
            settings.ContainsKey("PortName").ShouldBeFalse();
            settings["Parity"]!.GetValue<int>().ShouldBe(2);
            var after = graph.ToProjectJson();
            graph.Undo(); graph.ToProjectJson().ShouldBe(before);
            graph.Redo(); graph.ToProjectJson().ShouldBe(after);
            graph.LoadProject(after); graph.Connections.Single().Settings["Extra"]!["Keep"]!.GetValue<int>().ShouldBe(42);
        });
    }

    [Theory]
    [InlineData("ReadTimeout", 0, false)]
    [InlineData("ReadTimeout", 1, true)]
    [InlineData("ReadTimeout", 60000, true)]
    [InlineData("ReadTimeout", 60001, false)]
    [InlineData("WriteTimeout", 0, false)]
    [InlineData("WriteTimeout", 2147483647L, true)]
    [InlineData("WriteTimeout", 2147483648L, false)]
    public void Serial_connection_grid_and_registration_agree_on_timeout_limits(string name, long milliseconds, bool valid)
    {
        var description = StudioTestServices.SerialConnection();
        var settings = description.Create();
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var field = description.PropertyGroups!(settings).SelectMany(group => group.Options).Single(option => option.Name == name);
        (duration >= field.MinDuration && duration <= field.MaxDuration).ShouldBe(valid);
        settings[name] = duration.ToString("c");
        if (valid) Should.NotThrow(() => description.Validate!(settings)); else Should.Throw<ArgumentOutOfRangeException>(() => description.Validate!(settings));
    }
}
