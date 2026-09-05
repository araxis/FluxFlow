using System.Diagnostics;
using System.Globalization;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed class ReleaseTestProcessTests
{
    [Fact]
    public async Task RunAsync_normal_exit_returns_exact_exit_code_stdout_and_stderr()
    {
        var expectedOutput = new string('o', 131_072);
        var expectedError = new string('e', 131_072);
        using var fixture = ProcessFixture.Create(
            """
            [Console]::Out.Write('o' * 131072)
            [Console]::Error.Write('e' * 131072)
            exit 7
            """);

        var result = await ReleaseTestProcess.RunAsync(
            fixture.CreateStartInfo(),
            TimeSpan.FromSeconds(10),
            "normal test child");

        result.ExitCode.ShouldBe(7);
        result.StandardOutput.ShouldBe(expectedOutput);
        result.StandardError.ShouldBe(expectedError);
    }

    [Fact]
    public async Task RunAsync_timeout_throws_and_terminates_owned_process()
    {
        using var fixture = ProcessFixture.CreateBlocking();
        using var readiness = new MarkerReadiness(fixture.MarkerPath);

        var execution = ReleaseTestProcess.RunAsync(
            fixture.CreateStartInfo(fixture.MarkerPath),
            TimeSpan.FromSeconds(3),
            "timeout test child");

        await readiness.WaitAsync(TimeSpan.FromSeconds(5));
        var processId = await fixture.ReadProcessIdAsync();
        var error = await Should.ThrowAsync<TimeoutException>(async () =>
            await execution.WaitAsync(TimeSpan.FromSeconds(10)));

        error.Message.ShouldBe("timeout test child did not finish within 00:00:03.");
        ProcessShouldHaveExited(processId);
    }

    [Fact]
    public async Task RunAsync_cancellation_propagates_token_and_terminates_owned_process()
    {
        using var fixture = ProcessFixture.CreateBlocking();
        using var readiness = new MarkerReadiness(fixture.MarkerPath);
        using var cancellation = new CancellationTokenSource();

        var execution = ReleaseTestProcess.RunAsync(
            fixture.CreateStartInfo(fixture.MarkerPath),
            TimeSpan.FromSeconds(30),
            "cancelled test child",
            cancellation.Token);

        await readiness.WaitAsync(TimeSpan.FromSeconds(5));
        var processId = await fixture.ReadProcessIdAsync();
        cancellation.Cancel();
        var error = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await execution.WaitAsync(TimeSpan.FromSeconds(10)));

        error.CancellationToken.ShouldBe(cancellation.Token);
        ProcessShouldHaveExited(processId);
    }

    [Fact]
    public async Task RunAsync_rejects_non_positive_or_infinite_timeout()
    {
        var startInfo = new ProcessStartInfo("not-started");

        (await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            ReleaseTestProcess.RunAsync(startInfo, TimeSpan.Zero, "validation test")))
            .ParamName.ShouldBe("timeout");
        (await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            ReleaseTestProcess.RunAsync(startInfo, Timeout.InfiniteTimeSpan, "validation test")))
            .ParamName.ShouldBe("timeout");
        (await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            ReleaseTestProcess.RunAsync(startInfo, TimeSpan.MaxValue, "validation test")))
            .ParamName.ShouldBe("timeout");
    }

    [Fact]
    public async Task Release_script_runner_preserves_environment_override_and_removal()
    {
        using var fixture = ProcessFixture.CreateRepository(
            "environment.ps1",
            """
            param([string]$RemovedVariable)
            if ([Environment]::GetEnvironmentVariable($RemovedVariable) -ne $null) { exit 11 }
            [Console]::Out.WriteLine($env:RELEASE_TEST_OVERRIDE)
            """);
        var removedVariable = new[] { "TEMP", "TMP", "USERPROFILE", "HOME", "SystemRoot" }
            .First(name => Environment.GetEnvironmentVariable(name) is not null);
        var environment = new Dictionary<string, string?>
        {
            [removedVariable] = null,
            ["RELEASE_TEST_OVERRIDE"] = "override-value"
        };

        var result = await ReleaseScriptRunner.RunAsync(
            fixture.Root,
            "environment.ps1",
            environment,
            removedVariable);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldBe($"override-value{Environment.NewLine}");
        result.StandardError.ShouldBeEmpty();
    }

    private static void ProcessShouldHaveExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.HasExited.ShouldBeTrue($"process {processId} should have been terminated.");
        }
        catch (ArgumentException)
        {
            // A process that is no longer addressable has exited.
        }
    }

    private sealed class MarkerReadiness : IDisposable
    {
        private readonly string _markerPath;
        private readonly FileSystemWatcher _watcher;
        private readonly TaskCompletionSource _created =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MarkerReadiness(string markerPath)
        {
            _markerPath = markerPath;
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(markerPath)!, Path.GetFileName(markerPath));
            _watcher.Created += OnCreated;
            _watcher.Renamed += OnRenamed;
            _watcher.EnableRaisingEvents = true;
        }

        public Task WaitAsync(TimeSpan timeout)
        {
            if (File.Exists(_markerPath))
                _created.TrySetResult();

            return _created.Task.WaitAsync(timeout);
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnCreated;
            _watcher.Renamed -= OnRenamed;
            _watcher.Dispose();
        }

        private void OnCreated(object sender, FileSystemEventArgs arguments)
            => _created.TrySetResult();

        private void OnRenamed(object sender, RenamedEventArgs arguments)
            => _created.TrySetResult();
    }

    private sealed class ProcessFixture : IDisposable
    {
        private ProcessFixture(string root, string scriptPath, string markerPath)
        {
            Root = root;
            ScriptPath = scriptPath;
            MarkerPath = markerPath;
        }

        public string Root { get; }
        public string ScriptPath { get; }
        public string MarkerPath { get; }

        public static ProcessFixture Create(string script)
        {
            var root = CreateTemporaryDirectory();
            var scriptPath = Path.Combine(root, "process.ps1");
            File.WriteAllText(scriptPath, script);
            return new ProcessFixture(root, scriptPath, Path.Combine(root, "ready.pid"));
        }

        public static ProcessFixture CreateBlocking()
        {
            var fixture = Create(
                """
                param([string]$MarkerPath)
                $hostPath = (Get-Process -Id $PID).Path
                & $hostPath -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'descendant.ps1') $MarkerPath
                """);
            File.WriteAllText(
                Path.Combine(fixture.Root, "descendant.ps1"),
                """
                param([string]$MarkerPath)
                $temporaryMarker = "$MarkerPath.tmp"
                [IO.File]::WriteAllText($temporaryMarker, $PID.ToString([Globalization.CultureInfo]::InvariantCulture))
                [IO.File]::Move($temporaryMarker, $MarkerPath)
                [Threading.ManualResetEventSlim]::new($false).Wait()
                """);
            return fixture;
        }

        public static ProcessFixture CreateRepository(string scriptName, string script)
        {
            var root = CreateTemporaryDirectory();
            var eng = Directory.CreateDirectory(Path.Combine(root, "eng"));
            var scriptPath = Path.Combine(eng.FullName, scriptName);
            File.WriteAllText(scriptPath, script);
            return new ProcessFixture(root, scriptPath, Path.Combine(root, "ready.pid"));
        }

        public ProcessStartInfo CreateStartInfo(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(ReleaseTestPaths.FindScriptHost())
            {
                WorkingDirectory = Path.GetTempPath(),
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(ScriptPath);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            return startInfo;
        }

        public async Task<int> ReadProcessIdAsync()
        {
            var text = await File.ReadAllTextAsync(MarkerPath);
            return int.Parse(text, CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), $"fluxflow-process-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
