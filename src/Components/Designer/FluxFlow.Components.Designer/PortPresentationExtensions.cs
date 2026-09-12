using FluxFlow.Components.Designer.Contracts;

namespace FluxFlow.Components.Designer;

public static class PortPresentationExtensions
{
    public static ComponentDesignMetadata WithPort(this ComponentDesignMetadata metadata,
        string name, PortDirection direction, string? displayName = null, string? group = null,
        int? order = null, string? summary = null, bool? isPrimary = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = new ComponentPortName(name.Trim());
        return metadata with
        {
            Ports = MetadataEdit.Replace(metadata.Ports,
                port => port.Name == key && port.Direction == direction, port => port with
                {
                    DisplayName = displayName is null ? port.DisplayName : MetadataEdit.Text(displayName),
                    Group = group is null ? port.Group : new(group),
                    Order = order ?? port.Order,
                    Summary = summary is null ? port.Summary : MetadataEdit.Text(summary),
                    IsPrimary = isPrimary ?? port.IsPrimary
                }, $"Component {direction} port '{key}' is not registered.")
        };
    }
}
