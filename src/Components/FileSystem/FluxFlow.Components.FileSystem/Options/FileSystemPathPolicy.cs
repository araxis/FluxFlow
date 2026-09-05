namespace FluxFlow.Components.FileSystem.Options;

internal sealed record FileSystemPathPolicy(
    string NodeType,
    string? BaseDirectory,
    bool AllowAbsolutePaths,
    int InvalidPathCode,
    int AbsolutePathDeniedCode);
