using System.Diagnostics;

namespace FluxFlow.Release.Tests;

internal static class ReleaseScriptRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public static async Task<ReleaseScriptResult> RunAsync(
        string root,
        string scriptName,
        params string[] arguments)
        => await RunAsync(root, scriptName, environment: null, arguments);

    public static async Task<ReleaseScriptResult> RunAsync(
        string root,
        string scriptName,
        IReadOnlyDictionary<string, string?>? environment,
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

        if (environment is not null)
        {
            foreach (var entry in environment)
            {
                if (entry.Value is null)
                    startInfo.Environment.Remove(entry.Key);
                else
                    startInfo.Environment[entry.Key] = entry.Value;
            }
        }

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

        var result = await ReleaseTestProcess.RunAsync(
            startInfo,
            DefaultTimeout,
            $"release script '{Path.GetFileName(scriptPath)}'");

        return new ReleaseScriptResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError);
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
