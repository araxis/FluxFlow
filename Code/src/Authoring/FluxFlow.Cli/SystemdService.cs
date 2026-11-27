using System.Diagnostics;
using System.Security.Cryptography;

namespace FluxFlow.Cli;

internal static class SystemdService
{
    internal static void ValidateName(string name)
    {
        if (name.Length is 0 or > 63 || !char.IsAsciiLetterOrDigit(name[0]) ||
            name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("Use a Linux service name of 1 to 63 letters, digits, hyphens or underscores, starting with a letter or digit.");
    }

    internal static int Install(string name, string source, string feed, string startup, CancellationToken token)
    {
        var staging = Directory.CreateTempSubdirectory("fluxflow-service-");
        var app = Path.Combine(staging.FullName, "App");
        try
        {
            PublishCommand.WriteExecutableAsync(app, source, feed, "linux-x64", token).GetAwaiter().GetResult();
            using var executable = File.OpenRead(app);
            token.ThrowIfCancellationRequested();
            return Control("install", name, startup, executable);
        }
        finally
        {
            File.Delete(app);
            staging.Delete();
        }
    }

    internal static int Control(string action, string name, string startup = "manual", Stream? executable = null)
    {
        if (!Directory.Exists("/run/systemd/system"))
            throw new InvalidOperationException("The destination must be running the system-wide systemd service manager.");
        using var resource = typeof(SystemdService).Assembly.GetManifestResourceStream("systemd-service.sh")!;
        using var reader = new StreamReader(resource);
        var info = new ProcessStartInfo { UseShellExecute = false, RedirectStandardInput = executable is not null };
        // Never forward workflow JSON, environment settings or build commands to the privileged process.
        // The helper receives a fixed program, separate arguments and the verified executable bytes.
        if (Environment.UserName == "root") info.FileName = "/bin/sh";
        else
        {
            var graphical = Console.IsInputRedirected && File.Exists("/usr/bin/pkexec") &&
                (Environment.GetEnvironmentVariable("DISPLAY") is not null || Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null);
            info.FileName = graphical ? "/usr/bin/pkexec" : "/usr/bin/sudo";
            if (graphical) info.ArgumentList.Add("--disable-internal-agent");
            if (!graphical && Console.IsInputRedirected) info.ArgumentList.Add("-n");
            info.ArgumentList.Add("/bin/sh");
        }
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(reader.ReadToEnd().ReplaceLineEndings("\n"));
        info.ArgumentList.Add("fluxflow-service");
        info.ArgumentList.Add(action);
        info.ArgumentList.Add(name);
        info.ArgumentList.Add(startup);
        if (executable is not null)
        {
            info.ArgumentList.Add(Convert.ToHexString(SHA256.HashData(executable)).ToLowerInvariant());
            executable.Position = 0;
        }
        using var process = Process.Start(info)!;
        if (executable is not null)
        {
            try { executable.CopyTo(process.StandardInput.BaseStream); }
            catch (IOException) { /* Authorization or duplicate checks can close the pipe early. */ }
            finally { process.StandardInput.Close(); }
        }
        process.WaitForExit();
        return process.ExitCode;
    }
}
