#:property TargetFramework=net10.0
#:property RollForward=Major
#:property PublishAot=false
#:property PublishTrimmed=false
#:property PublishSingleFile=true

using System.ComponentModel;
using System.Diagnostics;

// Let each child handle the console signal and clean up before returning to the caller.
using var canceled = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; canceled.Cancel(); };

var kit = Path.GetFullPath("..", AppContext.BaseDirectory);
try
{
    if (UsesKitManifest(kit))
    {
        var restore = Run(["tool", "restore", "--tool-manifest", Path.Combine(kit, ".config", "dotnet-tools.json"),
            "--configfile", Path.Combine(kit, "feed", "NuGet.Config")], restore: true);
        if (restore != 0) return restore;
    }
    return Run(["tool", "run", "dotnet-fluxflow", "--", .. args]);
}
catch (Win32Exception error)
{
    Console.Error.WriteLine($"Could not start dotnet. {error.Message}");
    return 1;
}

static bool UsesKitManifest(string kit)
{
    // Bootstrap this kit only. A nearer project manifest belongs to the caller.
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, ".config", "dotnet-tools.json")))
            return string.Equals(dir.FullName, kit, StringComparison.OrdinalIgnoreCase);
        if (File.Exists(Path.Combine(dir.FullName, "dotnet-tools.json"))) return false;
    }
    return false;
}

int Run(string[] arguments, bool restore = false)
{
    if (canceled.IsCancellationRequested) return 130;
    var command = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = restore };
    foreach (var argument in arguments) command.ArgumentList.Add(argument);
    using var child = Process.Start(command)!;
    var restoreOutput = restore ? child.StandardOutput.ReadToEnd() : null;
    child.WaitForExit();
    if (restore && child.ExitCode != 0) Console.Error.Write(restoreOutput);
    return canceled.IsCancellationRequested ? 130 : child.ExitCode;
}
