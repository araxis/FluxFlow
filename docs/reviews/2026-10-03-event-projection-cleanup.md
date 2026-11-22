# Event projection cleanup

Follow-up decision: typed and untyped event projection now both prefer a valid
payload timestamp and fall back to the envelope timestamp. This supersedes the
typed-envelope precedence preserved in the initial pass described below. Typed
overloads now delegate to the common reader, removing the timestamp-selection
flag. The envelope itself and its correlation ID are not modified. Callers needing
the envelope time explicitly can read message.Timestamp.
Follow-up validation: 2,031 Release tests pass across .NET 8/10 with zero warnings,
including both overloads' missing/invalid timestamp fallback and unchanged envelopes.

The remaining Core review found concrete correctness and readability improvements
in event conversion. Component lifetimes, diagnostics, metrics and the native block
helpers keep their current responsibilities; no replacement abstraction was needed.

## Changes

- FlowEvent.cs retains the event model, its existing enums/header conventions and
  message encoding. The existing FlowEventMessageExtensions and
  FlowEventDataflowExtensions now live in FlowEventExtensions.cs. This separates
  reading/posting helpers from the model without introducing types or public APIs.
- Typed message projection uses the shared reader directly, selecting envelope time
  during construction instead of allocating a second event record to overwrite it.
  Untyped projection still prefers payload time, and both retain envelope correlation.
  Error messages still fail event projection without accessing a typed error payload.
- Event numeric attributes/dimensions retain Int64 values. The previous conditional
  expression promoted its integer branch to double before boxing, losing precision
  for large integers. Boxing the integer branch explicitly also fixes nested arrays
  and objects. Fractional values retain double representation.
- Optional timestamp and measurement parsing checks JSON kind before native TryGet
  calls. Invalid timestamp types now use the existing fallback; nonnumeric measurement
  entries are skipped. This follows the reader's existing optional-field defaults.
- Event encoding trims the name once for both payload and headers. Snapshot behavior,
  measurement validation, formatting fallbacks and wire field names are unchanged.

## Verification

New regression tests cover wrong-type timestamps, mixed-type measurements and exact
Int64 boundaries in attributes, dimensions and nested values. Seven cases failed on
each target before the fixes; an invalid string timestamp already passed. The focused
event/value tests then passed 68 executions across .NET 8 and .NET 10, with no warnings.

Public declarations are unchanged; their source snapshot was refreshed because
the baseline includes source filenames. No commit or publication was performed.

Final verification: all 2,029 Release test executions pass across .NET 8/10, with
zero warnings. The Debug solution build also succeeds with zero errors/warnings.
The isolated Core package consumer passes both targets. Full test evidence is in
the local temporary directory flux-event-projection-final; package evidence is in
fluxflow-core-consumer-b146583f609641cdb09be90cfedb6447.
