# Direct registration of one typed link

The user requested a connection API independent of OneOrManyLinks. Added the single
AddLink(IServiceCollection, PortAddress source, PortAddress target) extension in
Configuration.Links. It registers one unconditional connection. No collection, temporary
configuration section, new model or string overload is required.

The old per-item loop body is now the shared private RegisterLink implementation.
Plural input/output/error helpers choose direction, then use that same implementation
with their existing condition, engine and error filter. Direct links use the same
canonical typed identity and native keyed LinkFactory plus unkeyed execution alias.
Factories are not invoked during registration and no second provider is introduced.

This supports one connection; it does not enforce that no other link may target/source
the same port. Any actual component-specific cardinality rule belongs to its registrar.
There was no request to impose such a restriction globally.

Validation: Configuration passed 291 cases on each of .NET 8 and .NET 10. New integration
cases exercise direct-only registration, duplicate direct declarations, declarations
from both configuration directions, both registration orders, deferred construction and
exactly-once delivery. System destinations still reject without mutating services.
Public API baseline updated and its two checks passed. Existing condition/error tests pass.
The isolated packaged consumer passed on .NET 8.0.31 and .NET 10.0.12; direct and
two-sided configuration declarations still resolve to one registered connection.

Evidence: artifacts/pre-setup-review-20260923/single-link-{tests,api,package}.log.
No commit, merge or publication performed.
