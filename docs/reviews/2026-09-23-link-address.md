# Link Address naming

Renamed LinkConfiguration.Port to Address and migrated the section reader, registration,
tests, current configuration guides and isolated package consumer. Address is the other
port's reference: target for Output declarations and source for Input declarations.
Resolution still belongs to ApplicationAddress.ResolvePort. No address wrapper or new
lookup is introduced. Scalar shorthand and case-insensitive field names are unchanged.
Old Port fields are rejected by existing strict validation. Historical design reports
remain historical. Rebuild consumers and update named arguments/property access and
configuration fields.

Delivery and Fallback have not changed. Reliable waits for admission capacity;
RejectWhenFull attempts immediate admission and faults on rejection. IsolateRejection
reports rejection or a thrown condition through a required callback without faulting
the graph, but does not isolate component lifetime failures. The current JSON link
registrar cannot supply that callback, so IsolateRejection fails Build on that path.
Fallback applies only when no ordinary route matches, not after failed delivery or
downstream processing. All matching ordinary routes receive the message; at most one
unconditional fallback is allowed per output. Admission is not processing success,
durability or exactly-once delivery.

Validation: Configuration tests passed 231 per target on .NET 8/10 (462 executions),
including scalar/object/mixed arrays, both declaration directions, lower-case address
and old Port rejection. Public API baseline updated and its check passed.
The isolated packaged Configuration consumer passed on .NET 8.0.31 and .NET 10.0.12;
diff whitespace checks passed. Logs: artifacts/cleanup-review-20260920/link-address-*.log.
No Core/runtime change. No commit/publication.
