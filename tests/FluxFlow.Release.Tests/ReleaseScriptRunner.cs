using System.Diagnostics;

namespace FluxFlow.Release.Tests;

internal static class ReleaseScriptRunner
{
    public static async Task<ReleaseScriptResult> RunAsync(
        string root,
        string scriptName,
        params string[] arguments)
    {
        var executable = ReleaseTestPaths.FindScriptHost();
        var scriptPath = Path.Combine(root, "eng", scriptName);
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ReleaseScriptResult(process.ExitCode, standardOutput, standardError);
    }
}

internal sealed record ReleaseScriptResult(int ExitCode, string StandardOutput, string StandardError)
{
    public override string ToString()
        => $"""
            Exit code: {ExitCode}
            Output:
            {StandardOutput}
            Error:
            {StandardError}
            """;
}
