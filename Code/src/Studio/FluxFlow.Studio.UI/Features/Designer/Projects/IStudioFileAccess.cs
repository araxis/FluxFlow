namespace FluxFlow.Studio.UI.Features.Designer.Projects;

public interface IStudioFileAccess
{
    // A cancelled picker returns null. Opening a file does not replace the editor contents.
    Task<StudioFile?> OpenAsync(string extension);
    // No file-type filter. Keep every UTF-8 character, including an initial BOM.
    Task<StudioFile?> OpenVerbatimTextAsync() => OpenAsync("");
    Task<StudioImageFile?> OpenImageAsync();
    Task<StudioFile?> SaveAsync(StudioFile file, bool saveAs = false);
    Task ManageWindowsServiceAsync(StudioServiceAction request, string configuration, IReadOnlyDictionary<string, Func<Stream>> sources) =>
        throw new NotSupportedException("Windows service management is unavailable in this host.");
    Task GenerateApplicationSourceAsync(string configuration) =>
        throw new NotSupportedException("Generating App.cs is unavailable in this host.");
    Task PublishCompiledApplicationAsync(string configuration) =>
        throw new NotSupportedException("Publishing a compiled application is unavailable in this host.");
    Task PublishCompiledApplicationAsync(string configuration, int targetMajor) =>
        targetMajor == 11 ? PublishCompiledApplicationAsync(configuration)
            : throw new NotSupportedException($"Publishing for .NET {targetMajor} is unavailable in this host.");
    Task PublishCompiledApplicationAsync(string configuration, int targetMajor, string suggestedName) =>
        PublishCompiledApplicationAsync(configuration, targetMajor);
    Task PublishCompiledApplicationAsync(string configuration, CompiledPublishTarget target, string suggestedName) =>
        target.Runtime == "win-x64" ? PublishCompiledApplicationAsync(configuration, target.Major, suggestedName)
            : throw new NotSupportedException($"Publishing for {target.Runtime} is unavailable in this host.");
    Task PublishApplicationAsync(string name, string configuration, int startupTimeoutSeconds) =>
        throw new NotSupportedException("Publishing is unavailable in this host.");
}

public sealed record StudioServiceAction(string Action, string Name, int Major, bool Automatic);

public sealed record CompiledPublishTarget(int Major = 11, string Runtime = "win-x64")
{
    public bool IsLinux => Runtime == "linux-x64";
    public string PlatformName => IsLinux ? "Linux x64" : "Windows x64";
    public string FileName(string stem) => IsLinux ? stem : stem + ".exe";
}

// Location is supplied and interpreted by the host, never by the shared UI.
public sealed record StudioFile(string Name, string Content, string? Location = null);

public sealed record StudioImageFile(string Name, string MediaType, string Base64);
