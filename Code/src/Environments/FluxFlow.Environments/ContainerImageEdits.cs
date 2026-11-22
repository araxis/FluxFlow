namespace FluxFlow.Environments;

/// <summary>Image edits for a reusable profile (registry ID) or a project binding (registry address).</summary>
public sealed class ContainerImageEdits(ContainerProfile profile, IReadOnlyList<ImageRegistry> registries,
    bool copyRegistryAddress = false, string? defaultImage = null)
{
    private readonly string _image = profile.Settings.Image ?? defaultImage ?? "";
    public ContainerImageReference Reference { get; } = ContainerImageReference.Parse(profile.Settings.Image ?? defaultImage);
    public ImageRegistry? Registry => copyRegistryAddress
        ? registries.FirstOrDefault(item => string.Equals(item.Address, Reference.Registry, StringComparison.OrdinalIgnoreCase))
        : registries.FirstOrDefault(item => item.Id == profile.RegistryId);
    public bool Locked => profile.Settings.Profile?.SetupLocked == true;
    public bool CanEditReference => !Locked && Registry is not null;

    public ContainerProfile SelectRegistry(string id)
    {
        if (Locked || Registry?.Id == id || !registries.Any(item => item.Id == id)) return profile;
        return WriteReference(id, ContainerImageReference.WithoutRegistry(_image));
    }

    public ContainerProfile SetName(string? name) => Change(Reference with { Name = (name ?? "").Trim() });
    public ContainerProfile SetVersion(string? version) => Change(Reference with { Version = (version ?? "").Trim() });
    private ContainerProfile Change(ContainerImageReference next) =>
        !CanEditReference || next == Reference ? profile : WriteReference(Registry!.Id, next.RelativeAddress);

    private ContainerProfile WriteReference(string id, string relative)
    {
        var updated = profile with { RegistryId = id, Settings = profile.Settings with { Image = relative } };
        return copyRegistryAddress
            ? profile with { Settings = profile.Settings with { Image = updated.ResolveImage(registries) } }
            : updated;
    }

    public ContainerProfile SetPullIfMissing(bool value) => Locked || value == profile.Settings.PullIfMissing
        ? profile : profile with { Settings = profile.Settings with { PullIfMissing = value } };
}
