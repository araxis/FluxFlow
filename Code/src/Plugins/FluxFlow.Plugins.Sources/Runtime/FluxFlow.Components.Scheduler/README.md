# FluxFlow.Components.Scheduler

Workflow configuration and registration are provided by the FluxFlow.Plugins.Sources package.

```csharp
await using var scheduler = new Scheduler(new SchedulerOptions
{
    NextOccurrence = Schedules.Every(TimeSpan.FromSeconds(5))
    // Or Schedules.At(instant), or Schedules.Cron("*/5 * * * *")
});
scheduler.Output.LinkTo(source.Input, new() { PropagateCompletion = true });
await scheduler.StartAsync();
// Later: scheduler.Complete(); await scheduler.Completion;
```

Scheduler emits a new FlowMessage with JsonFlowValue.Null on every occurrence.
There is no input, reply/ack tracking, retry, or knowledge of downstream operations.
Source derives its sample-data message from each signal, retaining its lineage.
Each independent tick starts a new message identity. Source exhaustion does not
stop Scheduler; the owner stops it explicitly.

Every waits one interval before the first tick and follows successive due times.
OutputCapacity (default 128) bounds native broadcast admission. It does not wait
for consumer capacity or processing; slow consumers may miss signals. A delayed timer emits one signal; subsequent
overdue occurrences are skipped, with no catch-up burst. If an interval was missed,
its cadence restarts from the current time. This is not a real-time timer.
At emits once for a future instant; an already expired instant emits nothing.
Cron uses Cronos, UTC by default, an optional TimeZoneInfo, and five fields (or six
when includeSeconds is true). Cronos owns calendar/DST calculation.

NextOccurrence can also be a caller-supplied function: return an instant strictly
after its argument, or null to finish. It may be queried again after a missed time;
it should calculate times without performing business work. Invalid results or
exceptions fault production through the existing source lifecycle and diagnostics;
they are not retried. TimeProvider defaults to System and can be supplied for tests.
Complete/StopProduction cancel pending delays; already accepted output drains.
Cronos belongs only to this optional component module, never Core.
