using System.Text;
using System.Text.Json;
using System.Xml;
using FluxFlow.Publishing;

namespace FluxFlow.Cli;

internal static class PublishCommand
{
    internal const string Usage = "Usage: dotnet fluxflow publish <application.json> --framework net10.0|net11.0 [--runtime win-x64|linux-x64] --feed <folder> --output <executable>";

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is ["publish", "--help" or "-h"])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 2; i + 1 < args.Length; i += 2)
        {
            if (args[i] is not ("--framework" or "--feed" or "--output" or "--runtime") ||
                !options.TryAdd(args[i], args[i + 1])) break;
        }
        var runtime = options.GetValueOrDefault("--runtime", "win-x64");
        if (args.Length != 2 + 2 * options.Count || options.GetValueOrDefault("--framework") is not ("net10.0" or "net11.0")
            || !options.ContainsKey("--feed") || !options.ContainsKey("--output") || runtime is not ("win-x64" or "linux-x64"))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        void Cancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += Cancel;
        try
        {
            var input = Path.GetFullPath(args[1]);
            var feed = Path.GetFullPath(options["--feed"]);
            var output = Path.GetFullPath(options["--output"]);
            if (runtime == "win-x64" && !string.Equals(Path.GetExtension(output), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an output path with an .exe extension.");
            if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an output path different from the application export.");

            var packages = Directory.GetFiles(feed, "*.nupkg").Order(StringComparer.Ordinal);
            var source = Program.GenerateFromFile(input, packages, options["--framework"], runtime);
            var token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await AtomicFile.WriteAsync(output, async temporary =>
            {
                using var destination = File.Create(temporary);
                await CompiledApplicationPublisher.PublishAsync(destination, feed, runtime, async (stream, ct) =>
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                    await writer.WriteAsync(source.AsMemory(), ct);
                }, token);
                if (!OperatingSystem.IsWindows() && runtime == "linux-x64")
                    File.SetUnixFileMode(temporary, File.GetUnixFileMode(temporary) | UnixFileMode.UserExecute);
            }, token);
            Console.WriteLine(output);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Publish canceled.");
            return 130;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or FormatException or JsonException or XmlException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= Cancel; }
    }
}
