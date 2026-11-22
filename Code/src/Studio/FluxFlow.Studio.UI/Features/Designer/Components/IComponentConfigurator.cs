using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace FluxFlow.Studio.UI.Features.Designer.Components;

public interface IComponentConfigurator
{
    string Component { get; }
    Type ComponentType { get; }
    bool RendersPropertyRows => false;
    bool ProvidesTools => false;
    bool ProvidesComponentJson => false;
}

public sealed record ComponentConfigurator<T>(string Component, bool RendersPropertyRows = false, bool ProvidesTools = false, bool ProvidesComponentJson = false) : IComponentConfigurator where T : ComponentConfiguratorComponent
{
    public Type ComponentType => typeof(T);
}

public abstract class ComponentConfiguratorComponent : ComponentBase
{
    [Parameter, EditorRequired] public IDictionary<string, JsonElement> Configuration { get; set; } = default!;
    [Parameter] public EventCallback OnChanged { get; set; }
    [Parameter] public EventCallback<FluxFlow.Authoring.OptionEditorModel> OnHelpChanged { get; set; }
    [Parameter] public object? ToolsSection { get; set; }
    public abstract Task OpenEditorAsync();
}
