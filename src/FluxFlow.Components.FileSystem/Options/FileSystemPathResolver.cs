namespace FluxFlow.Components.FileSystem.Options;

internal static class FileSystemPathResolver
{
    public static string Resolve(string requestPath, FileSystemPathPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(requestPath))
        {
            throw new FileSystemPathResolutionException(
                policy.InvalidPathCode,
                $"{policy.NodeType} request path cannot be empty.");
        }

        var isAbsolute = Path.IsPathRooted(requestPath);
        if (isAbsolute && !policy.AllowAbsolutePaths)
        {
            throw new FileSystemPathResolutionException(
                policy.AbsolutePathDeniedCode,
                $"{policy.NodeType} absolute paths are disabled.");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(policy.BaseDirectory))
            {
                return Path.GetFullPath(requestPath);
            }

            var baseDirectory = Path.GetFullPath(policy.BaseDirectory);
            if (!isAbsolute)
            {
                var resolvedPath = Path.GetFullPath(Path.Combine(baseDirectory, requestPath));
                if (!IsUnderBaseDirectory(baseDirectory, resolvedPath))
                {
                    throw new FileSystemPathResolutionException(
                        policy.InvalidPathCode,
                        $"{policy.NodeType} path escapes the configured baseDirectory.");
                }

                return resolvedPath;
            }

            return Path.GetFullPath(requestPath);
        }
        catch (FileSystemPathResolutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FileSystemPathResolutionException(
                policy.InvalidPathCode,
                $"{policy.NodeType} request path is invalid: {exception.Message}",
                exception);
        }
    }

    private static bool IsUnderBaseDirectory(string baseDirectory, string path)
    {
        var normalizedBase = Path.TrimEndingDirectorySeparator(baseDirectory);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return path.Equals(normalizedBase, comparison) ||
               path.StartsWith(normalizedBase + Path.DirectorySeparatorChar, comparison) ||
               path.StartsWith(normalizedBase + Path.AltDirectorySeparatorChar, comparison);
    }
}
