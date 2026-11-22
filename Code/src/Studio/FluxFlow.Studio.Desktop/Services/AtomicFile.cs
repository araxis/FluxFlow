namespace FluxFlow.Studio.Desktop.Services;

internal static class AtomicFile
{
    public static Task WriteAsync(string path, string content) =>
        WriteAsync(path, temporary => File.WriteAllTextAsync(temporary, content));

    public static async Task WriteAsync(string path, Func<string, Task> write, CancellationToken cancellation = default)
    {
        path = Path.GetFullPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await write(temporary);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
