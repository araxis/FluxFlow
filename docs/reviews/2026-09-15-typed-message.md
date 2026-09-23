# Typed payloads over one message envelope

FlowMessage<T> now inherits FlowMessage and constrains T to FlowValue. Every
canonical workflow port still uses FlowMessage. Sending a typed message through
those ports is an ordinary upcast: its payload instance and CLR type remain intact,
and routing does not request its lazy JSON. Generic message state no longer has
its own identity, headers, error representation and validation implementation.

The generic JSON converter delegates envelope parsing/writing to the canonical
converter. Typed reading uses the requested payload's System.Text.Json contract;
canonical reading continues to produce JsonFlowValue. Both write the payload's
FlowValue.Json representation. No CLR discriminator registry was added. The
existing message/identifier converters are public so source-generated metadata in
consumer assemblies can reference them. The generated-metadata regression passes
without a reflection-resolver fallback; Native AOT/trimming was not qualified.

Runtime send/request paths now pass the message directly rather than inspecting
its CLR type and serializing arbitrary payloads. Typed receive/observation retain
local JSON projection when the payload is not already the requested type. Link
conditions declare the common message type, independently of their input value
type. Existing native block registration and component base classes are unchanged.

Breaking preview changes: generic message/port/DI APIs require FlowValue;
successful null CLR payloads are rejected (use JsonFlowValue.Null); FlowMessage is
now the base record. Its typed record's equality contract is distinct from a
separately created base record, while an upcast is the same instance. The common
error payload remains available through FlowMessage.Value; typed Value throws on
failure. Typed error formatting does not access that throwing property.

Test/benchmark scalar scenarios use a fixture-only ScalarValue<T>, not a new Core
wrapper API. Existing scalar/link/shutdown tests were migrated rather than deleted.
Four additional cases (on both frameworks) cover typed fanout identity with zero
JSON builds, generated-metadata roundtrips, error formatting and generic constraints.
The package consumer also exercises typed request/reply without JSON materialization.

This completes the message step, not the planned simplification of node bases.
No benchmark performance claim, external application changes or publication.
Evidence is under artifacts/typed-message-20260915/.

Final qualification: 2,377 passed, zero failed/skipped, seven reports (the previous
2,369 plus eight executions of the four new tests). Release build has zero warnings
or errors. Fresh package-only consumers passed net8.0/net10.0, including typed
request/reply. Public API baseline and whitespace checks passed. Authoritative
evidence: verification.json, qualified-tests/, build-final.log and
qualified-consumer.log. Earlier exploratory runs are not added to this total.
