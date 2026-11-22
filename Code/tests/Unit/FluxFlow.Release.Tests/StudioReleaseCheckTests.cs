using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed class StudioReleaseCheckTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Release_check_retains_diagnostics_and_restores_location_when_reporting_fails(bool failReport)
    {
        var directory = Directory.CreateTempSubdirectory("fluxflow-release-report-");
        try
        {
            var harness = Path.Combine(directory.FullName, "trial.ps1");
            await File.WriteAllTextAsync(harness, ReportHarness);
            var result = await RunHarness(harness,
                Path.Combine(ReleaseTestPaths.FindRepositoryRoot(), "eng", "check-studio-release.ps1"), failReport.ToString());
            result.ExitCode.ShouldBe(0, result.StandardOutput + result.StandardError);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory.FullName, "result.json")));
            var root = report.RootElement;
            root.GetProperty("RestoredLocation").GetBoolean().ShouldBeTrue();
            var failure = root.GetProperty("Failure").GetString()!;
            failure.ShouldContain("primary command failure");
            failure.ShouldContain("inner command detail");
            if (failReport) failure.ShouldContain("summary write failure");
            var output = Directory.GetDirectories(directory.FullName).Single();
            (await File.ReadAllTextAsync(Path.Combine(output, "toolchain.log"))).ShouldContain("inner command detail");
            if (!failReport)
            {
                using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "summary.json")));
                summary.RootElement.GetProperty("Status").GetString().ShouldBe("Failed");
                summary.RootElement.GetProperty("ErrorDetails").GetString().ShouldContain("inner command detail");
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("start-and-stop-fail")]
    [InlineData("cleanup-first-stop-fails")]
    [InlineData("exit-during-kill")]
    public async Task Published_check_preserves_failure_and_cleans_up_every_owned_run(string scenario)
    {
        var directory = Directory.CreateTempSubdirectory("fluxflow-release-failure-");
        try
        {
            var harness = Path.Combine(directory.FullName, "trial.ps1");
            await File.WriteAllTextAsync(harness, PublishedHarness);
            var result = await RunHarness(harness,
                Path.Combine(ReleaseTestPaths.FindRepositoryRoot(), "eng", "check-published-studio.ps1"), scenario);
            result.ExitCode.ShouldBe(0, result.StandardOutput + result.StandardError);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory.FullName, "result.json")));
            var root = report.RootElement;
            root.GetProperty("RestoredStore").GetString().ShouldBe("preserved-store");
            root.GetProperty("Killed").GetBoolean().ShouldBeTrue();
            root.GetProperty("Disposed").GetBoolean().ShouldBeTrue();
            var failure = root.GetProperty("Failure").GetString()!;
            if (scenario == "start-and-stop-fail")
            {
                failure.ShouldContain("original start failure");
                failure.ShouldContain("cleanup stop failure");
            }
            else if (scenario == "cleanup-first-stop-fails")
            {
                failure.ShouldContain("cleanup stop failure");
                root.GetProperty("StopCalls").GetInt32().ShouldBe(4);
            }
            else failure.ShouldBeEmpty();
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task<ReleaseTestProcessResult> RunHarness(string harness, params string[] arguments)
    {
        var info = new ProcessStartInfo(ReleaseTestPaths.FindScriptHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(harness);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return await ReleaseTestProcess.RunAsync(info, TimeSpan.FromSeconds(30), "release-check failure trial");
    }

    private const string ReportHarness = """
        param([string] $Target, [string] $FailReport)
        $ErrorActionPreference = 'Stop'
        # Only command failure reporting is exercised; no Windows publication is performed.
        Set-Variable IsWindows -Value $true -Force
        $originalLocation = (Get-Location).Path
        function dotnet { throw [InvalidOperationException]::new('primary command failure', [Exception]::new('inner command detail')) }
        function Set-Content {
            param($LiteralPath, [Parameter(ValueFromPipeline)]$Value)
            process {
                if ($FailReport -eq 'True' -and $LiteralPath.EndsWith('summary.json')) { throw 'summary write failure' }
                Microsoft.PowerShell.Management\Set-Content -LiteralPath $LiteralPath -Value $Value
            }
        }
        $failure = ''
        try { & $Target -ArtifactsRoot $PSScriptRoot }
        catch { $failure = $_.Exception.ToString() }
        $result = @{ Failure = $failure; RestoredLocation = ((Get-Location).Path -eq $originalLocation) } | ConvertTo-Json
        [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'result.json'), $result)
        """;

    // Exercise the script's control flow without requiring publication or starting a real server.
    private const string PublishedHarness = """
        param([string] $Target, [string] $Scenario)
        $ErrorActionPreference = 'Stop'
        $distribution = Join-Path $PSScriptRoot 'distribution'
        $store = Join-Path $PSScriptRoot 'store'
        $canvas = Join-Path $distribution 'site/wwwroot/_content/Z.Blazor.Diagrams'
        [void][IO.Directory]::CreateDirectory($canvas)
        [void][IO.Directory]::CreateDirectory((Join-Path $store 'active'))
        [IO.File]::WriteAllText((Join-Path $distribution 'site/wwwroot/index.html'), 'asset')
        [IO.File]::WriteAllText((Join-Path $canvas 'canvas.js'), 'asset')
        $env:FLUXFLOW_PLUGIN_DIRECTORY = 'preserved-store'
        $global:state = @{ Stops = 0; Killed = $false; Disposed = $false; Runs = @{} }
        function Start-Process {
            param($ArgumentList, $WorkingDirectory, $WindowStyle, [switch]$PassThru, $RedirectStandardOutput, $RedirectStandardError)
            [IO.File]::WriteAllText($RedirectStandardOutput, 'Now listening on: http://127.0.0.1:12345')
            $process = [pscustomobject]@{ HasExited = $false }
            $process | Add-Member ScriptMethod Kill {
                param($tree)
                $global:state.Killed = $true
                $this.HasExited = $true
                if ($Scenario -eq 'exit-during-kill') { throw [InvalidOperationException]::new('Process has exited') }
            }
            $process | Add-Member ScriptMethod WaitForExit { param($timeout) return $true }
            $process | Add-Member ScriptMethod Dispose { $global:state.Disposed = $true }
            return $process
        }
        function Invoke-RestMethod {
            param($Uri, $Method, $Headers, $TimeoutSec, $Body, $ContentType)
            if ($Uri.EndsWith('/plugins')) { return ,@() }
            if ($Uri.EndsWith('/check')) { return @{ Success = $true } }
            if ($Method -eq 'POST') {
                if ($Scenario -eq 'start-and-stop-fail') { throw 'original start failure' }
                $global:state.Runs[$Uri] = @{ State = 'Running'; Outputs = @{ Records = @(
                    @{ Port = 'main.gate.Output'; Message = @{ MessageId = "$Uri-first" } },
                    @{ Port = 'main.gate.Output'; Message = @{ MessageId = "$Uri-second" } }
                ) } }
            }
            if ($Method -eq 'DELETE') {
                $global:state.Stops++
                if ($Scenario -eq 'start-and-stop-fail' -or
                    ($Scenario -eq 'cleanup-first-stop-fails' -and $global:state.Stops -eq 3)) { throw 'cleanup stop failure' }
                $global:state.Runs[$Uri].State = 'Stopped'
                return
            }
            return $global:state.Runs[$Uri]
        }
        function Invoke-WebRequest {
            param($Uri, $OutFile, $Headers, $TimeoutSec)
            $text = if ($Uri.EndsWith('/telemetry/logs')) { 'Release check' } else { 'asset' }
            [IO.File]::WriteAllText($OutFile, $text)
        }
        $failure = ''
        try { & $Target -Distribution $distribution -PluginStore $store -Output $PSScriptRoot }
        catch { $failure = $_.Exception.ToString() }
        @{ Failure = $failure; StopCalls = $global:state.Stops; Killed = $global:state.Killed;
           Disposed = $global:state.Disposed; RestoredStore = $env:FLUXFLOW_PLUGIN_DIRECTORY
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'result.json')
        """;
}
