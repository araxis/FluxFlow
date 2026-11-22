namespace FluxFlow.Environments;

public sealed record ContainerImageReference(string? Registry, string Name, string Version, bool IsDigest)
{
    public string RelativeAddress => IsDigest ? $"{Name}@{Version}" : $"{Name}:{Version}";

    // Remove only the host; an untagged or pinned reference must keep its exact text.
    public static string WithoutRegistry(string address) =>
        Parse(address).Registry is { } host ? address[(host.Length + 1)..] : address;

    public static ContainerImageReference Parse(string? address)
    {
        var image = address ?? "";
        string? registry = null;
        var slash = image.IndexOf('/');
        if (slash > 0 && (image[..slash].Contains('.') || image[..slash].Contains(':') || image[..slash] == "localhost"))
        {
            registry = image[..slash];
            image = image[(slash + 1)..];
        }
        var digest = image.IndexOf('@');
        if (digest >= 0) return new(registry, image[..digest], image[(digest + 1)..], true);
        var tag = image.LastIndexOf(':');
        return tag >= 0 ? new(registry, image[..tag], image[(tag + 1)..], false) : new(registry, image, "latest", false);
    }
}
