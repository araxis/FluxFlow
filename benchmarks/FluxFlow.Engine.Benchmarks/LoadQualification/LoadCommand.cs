using System.Text.Json;

namespace FluxFlow.Engine.Benchmarks.LoadQualification;

internal static class LoadCommand
{
    internal static async Task RunAsync(string[] args)
    {
        var (options, output) = LoadOptions.Parse(args);
        var path = Path.GetFullPath(output);
        if (File.Exists(path)) throw new IOException("Preserve prior load evidence; choose a new output path.");
        var report = await LoadRunner.RunAsync(options);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(file, report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"{(options.Updates ? "updates" : "direct")}: {report.Completed:N0} completed; " +
            $"{report.CompletedPerSecond:N0}/s; p99 <= {report.AllTargetsLatency.P99UpperMilliseconds:N3} ms; " +
            $"pending after drain {report.PendingAfterDrain}. Report: {path}");
    }
}
