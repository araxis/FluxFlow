namespace FluxFlow.Release.Tests;

internal static class ReleaseTestPaths
{
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "eng", "packages.json")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    public static string FindScriptHost()
    {
        foreach (var candidate in new[] { "pwsh" })
        {
            var path = FindOnPath(candidate);
            if (path is not null)
                return path;
        }

        throw new InvalidOperationException("Could not find a script host on PATH.");
    }

    private static string? FindOnPath(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];

        foreach (var path in paths)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(path, fileName + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
