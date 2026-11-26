using System.ComponentModel;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;

namespace FluxFlow.Cli;

[SupportedOSPlatform("windows")]
internal static class WorkflowServiceInstaller
{
    internal static int Run(string name, string[] arguments, string? defaultFeed, string usage)
    {
        string? input = null;
        var interactive = false;
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < arguments.Length; i++)
        {
            var arg = arguments[i];
            if (arg == "--interactive" && !interactive) interactive = true;
            else if (!arg.StartsWith('-') && input is null) input = Path.GetFullPath(arg);
            else if (arg is "--feed" or "--framework" or "--startup" && i + 1 < arguments.Length &&
                     !arguments[i + 1].StartsWith('-') && options.TryAdd(arg, arguments[i + 1])) i++;
            else { Console.Error.WriteLine(usage); return 2; }
        }
        var framework = options.GetValueOrDefault("--framework", "net11.0");
        var startup = options.GetValueOrDefault("--startup", "manual");
        if (framework is not ("net10.0" or "net11.0") || startup is not ("manual" or "automatic"))
        {
            Console.Error.WriteLine(usage);
            return 2;
        }
        if (input is not null && Path.GetExtension(input).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (interactive || options.ContainsKey("--feed") || options.ContainsKey("--framework"))
                throw new ArgumentException("Discovery and build options require a workflow configuration.");
            return WindowsServiceCommand.Register(name, input, startup == "automatic");
        }
        if (name != name.Trim() || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Choose a service name suitable for an installation folder.");
        var feed = Path.GetFullPath(options.GetValueOrDefault("--feed") ?? defaultFeed ??
            throw new ArgumentException("Supply --feed <folder> with the pinned CLI, or use the installed FluxFlow SDK."));
        var generate = Program.CreateGenerator(Directory.GetFiles(feed, "*.nupkg").Order(StringComparer.Ordinal), framework, "win-x64");
        var source = input is null ? Discover(generate, interactive) : generate(input);
        if (source is null) return 130;

        using var service = new ServiceController(name, ".");
        try
        {
            _ = service.Status;
            throw new InvalidOperationException($"Service '{name}' is already registered.");
        }
        catch (InvalidOperationException error) when (error.InnerException is Win32Exception { NativeErrorCode: 1060 }) { }

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FluxFlow", "Services");
        var destination = Path.Combine(root, "service-" + name);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"The installation path already exists: {destination}");

        Directory.CreateDirectory(root);
        var owned = Directory.CreateDirectory(Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"))).FullName;
        using var cancellation = new CancellationTokenSource();
        void Cancel(object? sender, ConsoleCancelEventArgs e) { e.Cancel = true; cancellation.Cancel(); }
        Console.CancelKeyPress += Cancel;
        try
        {
            PublishCommand.WriteExecutableAsync(Path.Combine(owned, "App.exe"), source, feed, "win-x64", cancellation.Token)
                .GetAwaiter().GetResult();
            cancellation.Token.ThrowIfCancellationRequested();
            Directory.Move(owned, destination);
            owned = destination;
            cancellation.Token.ThrowIfCancellationRequested();
            var executable = Path.Combine(destination, "App.exe");
            var result = WindowsServiceCommand.Register(name, executable, startup == "automatic");
            if (result == 0)
            {
                owned = null;
                Console.WriteLine($"Installed '{name}' at {executable}");
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Service installation canceled.");
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
            if (owned is not null)
            {
                File.Delete(Path.Combine(owned, "App.exe"));
                Directory.Delete(owned);
            }
        }
    }

    private static string? Discover(Func<string, string> generate, bool interactive)
    {
        var candidates = new List<string>();
        string? source = null;
        foreach (var path in Directory.GetFiles(Environment.CurrentDirectory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var validated = generate(path);
                candidates.Add(path);
                source = candidates.Count == 1 ? validated : null;
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException
                or ArgumentException or FormatException or FileNotFoundException or DirectoryNotFoundException)
            {
                // Explicit paths report their validation error. Discovery only offers buildable exports.
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Skipping '{Path.GetFileName(path)}': {error.Message}");
            }
        }
        if (candidates.Count == 0)
            throw new InvalidDataException("No valid FluxFlow configuration found in the current folder. Pass the JSON file explicitly to validate it.");
        if (candidates.Count == 1)
        {
            Console.WriteLine($"Selected {Path.GetFileName(candidates[0])}");
            return source;
        }
        for (var i = 0; i < candidates.Count; i++) Console.WriteLine($"{i + 1}. {Path.GetFileName(candidates[i])}");
        if (!interactive)
            throw new ArgumentException("Multiple FluxFlow configurations found. Pass a file path or use --interactive.");
        while (true)
        {
            Console.Write("Select a configuration by number, or press Enter to cancel: ");
            var choice = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(choice)) return null;
            if (int.TryParse(choice, out var index) && index >= 1 && index <= candidates.Count)
                return generate(candidates[index - 1]);
        }
    }
}
