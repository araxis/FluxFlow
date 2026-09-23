# Message helper extraction

The user requested a bounded readability refactor of FlowMessage before revisiting
its model. Convenience operations now use the public envelope API from
FlowMessageExtensions: TryGetValue, TryGetError, Match, ConvertValue and ConvertError,
including typed conversion overloads. Conversions reuse the existing Restore APIs
and retain message identity, timestamp, correlation, causation and headers.

FlowMessage<T> is in the adjacent FlowMessageOfT.cs. Its Match<TResult> forwards
to the typed extension overload: keeping this small adapter preserves calls that
explicitly specify only TResult, which an extension with both T and TResult cannot
accept in that form. Factories, validation, With/WithError and identity generation
remain on the envelope. No new factory or interface was introduced.

FlowMessage.cs is 316 lines, down from 433. The typed file is 45 lines and helpers
are 76: 437 total versus 433 previously. This improves organization and removes
helper implementation from the envelope; it does not reduce the number of public
operations or claim a reduction in overall conceptual complexity.

Existing solution callers compile without edits. The extensions use the same
FluxFlow.Nodes namespace. This is nevertheless a binary-breaking preview change:
already compiled consumers must rebuild, and reflection/dynamic invocation of the
removed instance methods is not preserved. The API baseline records the change.
Null extension receivers throw ArgumentNullException. Structured headers remain
a proposal; their current string/string contract has not changed.

Validation: Release build, zero warnings/errors; 2,409 tests passed in nine TRX
reports; fresh Core-only and Routing package consumers pass .NET 8 and .NET 10.
Existing tests cover typed Match callbacks, JSON round-trips, conversion identity
and tracing propagation. Evidence: artifacts/message-helpers-20260917/.
No commit, push or publication.
