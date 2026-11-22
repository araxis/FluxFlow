using System.Diagnostics;

namespace FluxFlow.Publishing;

public static class DotnetProcess
{
    public static async Task RunAsync(ProcessStartInfo command, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        command.RedirectStandardError = command.RedirectStandardOutput = true;
        using var process = new Process { StartInfo = command };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new InvalidOperationException($"Could not start dotnet. {error.Message}", error);
        }
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellation); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        var output = await stdout;
        var errors = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet exited with code {process.ExitCode}.\n{output}{errors}".TrimEnd());
    }
}
