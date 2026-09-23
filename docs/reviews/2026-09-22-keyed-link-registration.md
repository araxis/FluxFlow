# Keyed link registration

LinkConfiguration.RegisterPort now registers directly in the staged IServiceCollection.
ApplicationConfiguration no longer allocates or passes a duplicate-link dictionary.
The private LinkKey record supplies value equality for full source/target addresses,
condition text, parameter signature and engine. The service key combines this identity
with delivery/fallback values, so existing LinkFactory descriptors support both duplicate
detection and conflicting-policy validation without a separate policy store.

Matching identity and policy skips registration. Matching identity with another policy
throws before the staged collection is published. TryAdd alone would silently ignore
conflicts, so registration explicitly examines the existing descriptor first.

Each accepted link has a keyed singleton delegate and one unkeyed alias resolving it.
FlowApplication still invokes only GetServices<LinkFactory>(); aliases introduce no
second execution path. Host unkeyed LinkFactory registrations remain supported.
No workflow grouping, public key API, provider, catalog or runtime table was added.

This replaces expected constant-time dictionary lookup with a scan of service
descriptors during registration, like standard TryAdd. It adds one keyed descriptor
per unique link. It is a simplification of registration ownership, not a performance
optimization. Runtime processing is unchanged.

Parameter conversion/signatures are unchanged: top-level names are sorted; nested
object ordering and exact condition/engine text retain their prior semantics.

Validation: full Release suite passed 1,818 executions across .NET 8/10, including
unchanged public API baseline. Extended setup tests verify one execution alias per
unique link, canonical delegate identity, duplicate declarations from opposite sides,
cross-workflow routing, no extra deliveries, compile reuse and policy conflicts.
Whitespace check passes. Evidence: artifacts/cleanup-review-20260920/keyed-link-full.log.
No package rerun for this internal registration change. No commit/publication.
