# Reusable single-link configuration values

The configuration DTO can now state whether a property accepts one link or a collection.
LinkConfiguration.Read reads one scalar address or Address/Condition/Engine object and
resolves its PortAddress within the component's workflow. OneOrManyLinks delegates each
item to this same reader and aggregates invalid item diagnostics. Link parsing no longer
lives exclusively inside a collection type.

The existing component DTO binder recognizes LinkConfiguration in addition to OneOrManyLinks.
It supports public writable/init properties, renamed configuration properties, defaults
and the same host-root scope. Missing properties keep their defaults; single-link arrays
fail at the configuration boundary. Ordinary properties still use the native binder.
No general union binder, converter registry or intermediate model is introduced.

AddInputLink, AddOutputLink and AddErrorLink consume one bound value. They and the plural
helpers share the private single-link registration path. Existing AddLink(source, target)
remains the two-address unconditional API. Conditions, engine selection, Error filtering,
deferred construction and native DI duplicate identity are preserved. A single configuration
property does not restrict links declared elsewhere against the same port.

Tests cover scalar/object parsing, local/full/system addresses, snapshots, missing optional
properties, array rejection and property locations. Existing aggregate diagnostics remain
covered. Integration cases deliver through single input/output/error links with conditions
and verify duplicate suppression against equivalent plural declarations. The isolated
package consumer uses a single Input property alongside bundled collection-based outputs.

The public additions are the reader and three singular registration helpers. Existing
signatures remain unchanged. The public API baseline is updated.

Verification: 305 Configuration tests passed per .NET 8/10 target; the full Release suite
passed 2,080 test executions across 28 projects with zero warnings. The isolated package
consumer passed on .NET 8.0.31 and 10.0.12, exercising single/collection/direct link
deduplication and actual delivery. Whitespace/diff checks passed.

No commit or publication is part of this change.
