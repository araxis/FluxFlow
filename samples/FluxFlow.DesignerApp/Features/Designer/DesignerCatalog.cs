using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Persistence;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Timers.Composition;
using FluxFlow.Components.Validation.Composition;
using FluxFlow.Composition;
using FluxFlow.DesignerHost;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.DesignerApp.Features.Designer;

/// <summary>
/// Builds the design-time metadata catalog once from a representative set of
/// package-owned component registrations and exposes the host-model projections
/// the UI binds to. Adding a component family is a one-line service registration.
/// </summary>
public sealed class DesignerCatalog
{
    private readonly DesignerHostCatalog _host;

    public DesignerCatalog()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponents()
            .AddTimers()
            .AddSources()
            .AddRouting()
            .AddValidation()
            .AddHttp()
            .AddStorage()
            .AddMqtt();
        using var provider = services.BuildServiceProvider();
        var componentCatalog = provider.GetRequiredService<ComponentCatalog>();
        var catalog = provider.GetRequiredService<ComponentDesignMetadataCatalog>();

        Persistence = new DesignerApplicationPersistence(componentCatalog, catalog);
        _host = new DesignerHostCatalog(catalog);
        Palette = _host.CreatePaletteItems();
    }

    public IReadOnlyList<PaletteItemModel> Palette { get; }

    public DesignerApplicationPersistence Persistence { get; }

    public NodeInspectorModel? Inspector(string componentType) => _host.CreateInspector(componentType);

    public PaletteItemModel? Find(string componentType)
        => Palette.FirstOrDefault(item => string.Equals(item.ComponentType, componentType, StringComparison.Ordinal));
}
