using FluxFlow.Components.Control.Composition;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Http.Composition;
using FluxFlow.Components.Mqtt.Composition;
using FluxFlow.Components.Routing.Composition;
using FluxFlow.Components.Sources.Composition;
using FluxFlow.Components.Storage.Composition;
using FluxFlow.Components.Timers.Composition;
using FluxFlow.Components.Validation.Composition;
using FluxFlow.DesignerHost;

namespace FluxFlow.DesignerApp.Features.Designer;

/// <summary>
/// Builds the design-time metadata catalog once from a representative set of
/// package-owned metadata providers and exposes the host-model projections the
/// UI binds to. Adding a component family is a one-line provider addition.
/// </summary>
public sealed class DesignerCatalog
{
    private readonly DesignerHostCatalog _host;

    public DesignerCatalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders(
        [
            new TimersComponentDesignMetadataProvider(),
            new SourcesComponentDesignMetadataProvider(),
            new RoutingComponentDesignMetadataProvider(),
            new ControlComponentDesignMetadataProvider(),
            new ValidationComponentDesignMetadataProvider(),
            new HttpComponentDesignMetadataProvider(),
            new StorageComponentDesignMetadataProvider(),
            new MqttComponentDesignMetadataProvider(),
        ]);

        _host = new DesignerHostCatalog(catalog);
        Palette = _host.CreatePaletteItems();
    }

    public IReadOnlyList<PaletteItemModel> Palette { get; }

    public NodeInspectorModel? Inspector(string componentType) => _host.CreateInspector(componentType);

    public PaletteItemModel? Find(string componentType)
        => Palette.FirstOrDefault(item => string.Equals(item.ComponentType, componentType, StringComparison.Ordinal));
}
