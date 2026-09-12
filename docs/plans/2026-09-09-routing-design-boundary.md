# Routing optional presentation boundary

Status: implementation in progress under the approved full core simplification.

| Package | Disposition | Targets and references |
|---|---|---|
| FluxFlow.Components.Routing | Retain constructors, nodes, clocks, selectors and matching behavior unchanged | Existing net8.0/net10.0 and references |
| FluxFlow.Components.Routing.Composition | Retain runtime descriptors and trusted portable authoring; remove presentation reference | Routing runtime and Composition; net8.0/net10.0 |
| FluxFlow.Components.Routing.Designer | New optional descriptor presentation through AddRoutingDesign | Routing.Composition and Components.Designer; net8.0/net10.0; 1.0.0-rc.1 |

Keep runtime AddRouting independent; visual consumers call AddRoutingDesign after
registration. No new creator, activation owner, scheduler or universal metadata
builder. Preserve all existing hints/defaults/resources/ports. Update designer
sample, release fixtures, manifest, destination map, solution, individually
reviewed API and package documentation together. Archive inspection is local slice
evidence only, not a full candidate feed or isolated consumer acceptance.
