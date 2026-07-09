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
                if (policy.AllowAbsolutePaths)
                {
                    return Path.GetFullPath(requestPath);
                }

                return ResolveUnderBaseDirectory(
                    requestPath,
                    System.IO.Directory.GetCurrentDirectory(),
                    policy,
                    "the working directory");
            }

            var baseDirectory = Path.GetFullPath(policy.BaseDirectory);
            if (!isAbsolute)
            {
                return ResolveUnderBaseDirectory(
                    requestPath,
                    baseDirectory,
                    policy,
                    "the configured baseDirectory");
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

    private static string ResolveUnderBaseDirectory(
        string requestPath,
        string baseDirectory,
        FileSystemPathPolicy policy,
        string baseDescription)
    {
        var resolvedBase = Path.GetFullPath(baseDirectory);
        var resolvedPath = Path.GetFullPath(Path.Combine(resolvedBase, requestPath));
        if (!IsUnderBaseDirectory(resolvedBase, resolvedPath))
        {
            throw new FileSystemPathResolutionException(
                policy.InvalidPathCode,
                $"{policy.NodeType} path escapes {baseDescription}.");
        }

        RejectLinkedDescendants(resolvedBase, resolvedPath, policy, baseDescription);

        return resolvedPath;
    }

    private static void RejectLinkedDescendants(
        string resolvedBase,
        string resolvedPath,
        FileSystemPathPolicy policy,
        string baseDescription)
    {
        var relativePath = Path.GetRelativePath(resolvedBase, resolvedPath);
        if (relativePath == ".")
            return;

        var current = resolvedBase;
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                throw new FileSystemPathResolutionException(
                    policy.InvalidPathCode,
                    $"{policy.NodeType} could not validate the path under {baseDescription}: {exception.Message}",
                    exception);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new FileSystemPathResolutionException(
                    policy.InvalidPathCode,
                    $"{policy.NodeType} path contains a symbolic link or reparse point under {baseDescription}.");
            }
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
