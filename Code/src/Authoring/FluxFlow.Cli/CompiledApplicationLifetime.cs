namespace FluxFlow.Cli;

internal static class CompiledApplicationLifetime
{
    internal const string Source = """
        OperationCanceledException? drainTimeout = null;
        try
        {
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
            await host.StartAsync();
            var app = host.Services.GetRequiredService<FlowApplication>();
            try
            {
                async Task WaitForEnterAsync()
                {
                    if (await Task.Run(Console.ReadLine) is null)
                        await stopping.Task;
                }
                await await Task.WhenAny(app.Completion, stopping.Task, WaitForEnterAsync());
            }
            finally
            {
                // Until workflow cleanup returns, an expired service-manager wait cannot confirm success.
                setServiceExit?.Invoke(1460); // ERROR_TIMEOUT
                using var grace = new CancellationTokenSource(drainGrace);
                try
                {
                    await app.StopAsync(grace.Token);
                    setServiceExit?.Invoke(0);
                }
                catch (OperationCanceledException error) when (grace.IsCancellationRequested && error.CancellationToken == grace.Token)
                {
                    drainTimeout = error;
                    Console.Error.WriteLine("Shutdown incomplete: the workflow did not drain before the deadline.");
                    Environment.ExitCode = 2;
                }
                finally
                {
                    try { await host.StopAsync(); }
                    catch (Exception error) { ReportCleanupError(error); }
                }
            }
        }
        finally
        {
            try { host.Dispose(); }
            catch (Exception error) { ReportCleanupError(error); }
        }

        void ReportCleanupError(Exception error)
        {
            // Stop and disposal can surface the same workflow deadline again.
            if (drainTimeout is not null && error is OperationCanceledException canceled &&
                canceled.CancellationToken == drainTimeout.CancellationToken) return;
            if (error is AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                    ReportCleanupError(inner);
                return;
            }
            Console.Error.WriteLine(error);
            Environment.ExitCode = 1;
            setServiceExit?.Invoke(1064); // ERROR_EXCEPTION_IN_SERVICE
        }
        """;
}
