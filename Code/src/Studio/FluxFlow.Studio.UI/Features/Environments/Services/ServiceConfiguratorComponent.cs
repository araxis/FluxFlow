using FluxFlow.Environments;
using Microsoft.AspNetCore.Components;

namespace FluxFlow.Studio.UI.Features.Environments.Services;

public abstract class ServiceConfiguratorComponent : ComponentBase
{
    [Parameter] public ResourceBinding Value { get; set; } = new();
    [Parameter] public EventCallback<ResourceBinding> ValueChanged { get; set; }
    [Parameter] public bool ReadOnly { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public Func<long>? CurrentRevision { get; set; }
    [Parameter] public Func<IDisposable>? BeginEdit { get; set; }
    protected Task Change(ResourceBinding value) => ReadOnly || Disabled ? Task.CompletedTask : ValueChanged.InvokeAsync(value);
}
