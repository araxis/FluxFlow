using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions.DataGenerator;
using FluxFlow.Authoring.Definitions.Retry;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.Studio.UI.Features.Designer.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Polly.Retry;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Theory]
    [InlineData("AttemptTimeout", "00:00:00", "-00:00:00.001")]
    [InlineData("Delay", "1.00:00:00.0000001", "00:00:00")]
    [InlineData("MaxDelay", "-00:00:00.001", "1.00:00:00")]
    [InlineData("MaxDelay", "-00:00:00.001", "")]
    [InlineData("Locale", "unknown-locale", "sv")]
    [InlineData("Locale", " ", "")]
    public async Task Retry_and_generator_fields_preserve_invalid_saved_values_until_repair(string property, string invalid, string repair)
    {
        var services = CreateServices(); var components = new CapturingComponents();
        services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        IComponentDescription description = property == "Locale" ? new DataGeneratorDescription() : new RetryDescription();
        var option = description.Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == property);
        var graph = scope.ServiceProvider.GetRequiredService<DesignerGraphState>();
        graph.LoadJson("{\"Workflows\":{\"main\":{\"item\":{\"Type\":\"" + description.Palette.ComponentType + "\",\"Extra\":42}}}}");
        var node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single();
        var key = property.ToLowerInvariant();
        node.Configuration[key] = JsonSerializer.SerializeToElement(invalid);
        graph.ResetHistory(); graph.AddEnvironment("Undo me"); graph.Undo();
        node = graph.Diagram.Nodes.OfType<FlowNodeModel>().Single(); graph.Diagram.SelectModel(node, true);
        var before = graph.ToProjectJson();
        await using (var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(OptionEditorField.Configuration)] = node.Configuration,
                    [nameof(OptionEditorField.Option)] = option,
                    [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, graph.RecordEdit)
                }));
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                var field = components.Items.OfType<ValidatedTextField>().Single();
                field.Validate(invalid).ShouldNotBeNull();
                var input = components.Items.OfType<MudTextField<string>>().Single();
                await input.ValueChanged.InvokeAsync(invalid);
                graph.ToProjectJson().ShouldBe(before); graph.CanRedo.ShouldBeTrue();
                field.Validate(repair).ShouldBeNull();
                await input.ValueChanged.InvokeAsync(repair);
            });
        }
        if (repair.Length == 0) graph.SelectedNode!.Configuration.ContainsKey(key).ShouldBeFalse();
        else graph.SelectedNode!.Configuration[key].GetString().ShouldBe(repair);
        graph.SelectedNode!.Configuration["Extra"].GetInt32().ShouldBe(42);
        var after = graph.ToProjectJson(); after.ShouldNotBe(before);
        graph.Undo(); graph.ToProjectJson().ShouldBe(before);
        graph.Redo(); graph.ToProjectJson().ShouldBe(after);
        graph.LoadProject(after); graph.ToProjectJson().ShouldBe(after);
    }

    [Theory]
    [InlineData("Delay")]
    [InlineData("MaxDelay")]
    public void Retry_delay_bounds_match_installed_policy_validation(string property)
    {
        var option = new RetryDescription().Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == property);
        var nativeProperty = typeof(RetryStrategyOptions<object>).GetProperty(property)!;
        foreach (var duration in new[] { TimeSpan.FromTicks(-1), TimeSpan.Zero, TimeSpan.FromDays(1), TimeSpan.FromDays(1) + TimeSpan.FromTicks(1) })
        {
            var policy = new RetryStrategyOptions<object>();
            nativeProperty.SetValue(policy, duration);
            var accepted = Validator.TryValidateObject(policy, new ValidationContext(policy), [], true);
            accepted.ShouldBe(duration >= option.MinDuration && duration <= option.MaxDuration);
        }
        option.AllowInfiniteDuration.ShouldBeFalse();
        option.IsRequired.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, true)]
    public void Retry_count_bounds_match_installed_policy_validation(int count, bool valid)
    {
        var option = new RetryDescription().Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == "MaxRetryAttempts");
        var policy = new RetryStrategyOptions<object> { MaxRetryAttempts = count };
        Validator.TryValidateObject(policy, new ValidationContext(policy), [], true).ShouldBe(valid);
        (count >= option.Min && count <= option.Max).ShouldBe(valid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("en", true)]
    [InlineData("sv", true)]
    [InlineData("unknown-locale", false)]
    [InlineData(" ", false)]
    public void Generator_locale_uses_existing_resource_validation(string? locale, bool valid)
    {
        var option = new DataGeneratorDescription().Inspector.Sections.SelectMany(section => section.Options).Single(item => item.Name == "Locale");
        (option.ValidateText!(locale) is null).ShouldBe(valid);
        if (!string.IsNullOrEmpty(locale)) Bogus.Database.LocaleResourceExists(locale).ShouldBe(valid);
    }
}
