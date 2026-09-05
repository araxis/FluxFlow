using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;

namespace FluxFlow.Release.Tests;

internal static class ReleaseTestProcess
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public static async Task<ReleaseTestProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan || timeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The process timeout must be positive and finite.");
        }

        startInfo.UseShellExecute = false;

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Could not start {operationName}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Could not start {operationName}.", exception);
        }

        var outputTask = startInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync()
            : Task.FromResult(string.Empty);
        var errorTask = startInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync()
            : Task.FromResult(string.Empty);

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        string[] streams;
        try
        {
            await process.WaitForExitAsync(waitCancellation.Token);
            streams = await Task.WhenAll(outputTask, errorTask)
                .WaitAsync(waitCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            var callerCancelled = cancellationToken.IsCancellationRequested;
            var cleanupFailure = await TerminateAsync(process, outputTask, errorTask, operationName);

            if (callerCancelled)
            {
                throw new OperationCanceledException(
                    $"{operationName} was cancelled.",
                    cleanupFailure,
                    cancellationToken);
            }

            throw new TimeoutException(
                $"{operationName} did not finish within {Format(timeout)}.",
                cleanupFailure);
        }
        catch (Exception exception)
        {
            var cleanupFailure = await TerminateAsync(
                process,
                outputTask,
                errorTask,
                operationName);

            if (cleanupFailure is null)
                ExceptionDispatchInfo.Capture(exception).Throw();

            throw new AggregateException(exception, cleanupFailure);
        }

        return new ReleaseTestProcessResult(process.ExitCode, streams[0], streams[1]);
    }

    private static async Task<Exception?> TerminateAsync(
        Process process,
        Task<string> outputTask,
        Task<string> errorTask,
        string operationName)
    {
        Exception? cleanupFailure = null;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await process.WaitForExitAsync(cleanupCancellation.Token);
            await Task.WhenAll(outputTask, errorTask).WaitAsync(cleanupCancellation.Token);
        }
        catch (OperationCanceledException) when (cleanupCancellation.IsCancellationRequested)
        {
            cleanupFailure = Combine(
                cleanupFailure,
                new TimeoutException(
                    $"{operationName} cleanup did not finish within {Format(CleanupTimeout)}."));
        }
        catch (Exception exception)
        {
            cleanupFailure = Combine(cleanupFailure, exception);
        }

        return cleanupFailure;
    }

    private static Exception Combine(Exception? first, Exception second)
        => first is null ? second : new AggregateException(first, second);

    private static string Format(TimeSpan duration)
        => duration.ToString("c", CultureInfo.InvariantCulture);
}

internal sealed record ReleaseTestProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
