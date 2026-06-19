namespace FluxFlow.Components.FileSystem.Options;

/// <summary>
/// Resolves the configured <see cref="FileWatchOptions.NotifyFilters"/> string values
/// into a single <see cref="NotifyFilters"/> flags value for the underlying
/// <see cref="FileSystemWatcher"/>.
/// </summary>
internal static class FileWatchNotifyFilters
{
    public static NotifyFilters Resolve(FileWatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.NotifyFilters.Length == 0)
        {
            return NotifyFilters.FileName |
                   NotifyFilters.DirectoryName |
                   NotifyFilters.LastWrite |
                   NotifyFilters.Size;
        }

        var filters = (NotifyFilters)0;
        foreach (var value in options.NotifyFilters)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Enum.TryParse<NotifyFilters>(value, ignoreCase: true, out var filter))
            {
                throw new ArgumentException(
                    $"file.watch option 'notifyFilters' contains unsupported value '{value}'.",
                    nameof(options));
            }

            filters |= filter;
        }

        return filters;
    }
}
