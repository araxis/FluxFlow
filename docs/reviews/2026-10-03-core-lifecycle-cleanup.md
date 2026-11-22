# Core lifecycle cleanup

This pass keeps the existing source and processing lifetime classes and their
different shutdown contracts. No new base class, interface, lifecycle manager,
dependency or public API is introduced.

## Changes

- FlowSourceLifetime.RunLifetimeAsync now reads as production/output completion,
  terminal-control completion, then event completion and the final result. The two
  phases are private methods within the same class. Failure state is named _failure
  because it includes production, output, control and cancellation failures;
  _controlStopping explicitly names the separate control cancellation source.
- FlowProcessingLifetime shares ordinary/terminal input observation and native
  task-failure handling. Ordinary inputs still drain before flushing; terminal
  controls close after outputs finish. Output monitoring still detects premature
  completion independently.
- FlowSource.EmitAsync directly returns the existing publication ValueTask instead
  of adding an async forwarding method. Native broadcast admission is unchanged.
- Processing disposal puts cancellation-source disposal in a finally block. A
  throwing cancellation callback previously skipped this cleanup. Its exception
  still reaches the caller and diagnostics, and successful Completion stays successful.

## Preserved behavior

Source StopProduction leaves terminal controls open for outstanding work. Natural
production completion does too. ProductionCompletion includes native data-output
completion; full Completion additionally joins controls and their cancellation
callbacks. A late control failure cannot rewrite an already completed production
task. Complete closes control admission; Fault cancels and faults active work.

Existing synchronization remains necessary for concurrent production, control
callbacks, faults and shutdown. Flush, first-failure reporting, disposal ownership,
native block identity and public component authoring contracts are preserved.

## Verification

The existing disposal diagnostic test was strengthened to verify that the owned
cancellation source is disposed after a callback throws. It failed on both .NET 8
and .NET 10 before the fix and passes afterward. The late-control-failure test now
covers both natural production completion and an explicit StopProduction.

The node test suite passes 608 executions across .NET 8 and .NET 10.

The first full run exposed a Batch test left over from the former delivery policy:
it assumed a full downstream consumer blocks publication. Under native broadcast
that assertion raced against processing. The test now verifies successful local
publication with a full consumer, with a separate test preserving explicit-fault
admission closure, exception identity and partial-batch discard. Batch production
code is unchanged.

The source-declaration baseline was refreshed for the removed async forwarding
modifier and an expression-bodied method inside the internal processing lifetime.
Neither changes the compiled public signatures.

Final Release validation: 1,999 test executions pass across .NET 8 and .NET 10,
with zero warnings. The isolated Core package consumer passes on both targets,
including standalone processing, message lineage, JSON and request/reply. The
full test results are in the local temporary directory flux-core-lifecycle-final;
package evidence is in fluxflow-core-consumer-3e6c0e6656694061936709e1683eeecc.
No commit or publication was performed.
