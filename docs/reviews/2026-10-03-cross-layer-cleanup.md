# Cross-layer cleanup review

Reviewed the registration-to-execution path across Core, Application and
Configuration after their separation and the single-node-base migration.

## Findings fixed

- FlowNode.AddOutput bypassed the construction guard used by additional inputs.
  It now checks the same boundary under the existing port lock, before allocating
  or attaching another block. Constructor-declared side outputs still complete
  with their node. No new lifecycle state or output wrapper was introduced.
- Resource traversal validated only the Resources root as an object. A nested
  scalar without Type was silently skipped. Every traversed resource/group section
  now uses the existing object check; errors include the section path and leave
  the caller's registrations unchanged. Resource-specific settings remain the
  responsibility of their registrar.
- In-memory or layered configuration can expose both a scalar and children at one
  path. Single and multiple port-reference readers previously ignored the scalar
  when reading an object/array. They now reject that ambiguous shape at the path.
  Supported scalar, object and array forms remain unchanged.
- Removed redundant ComponentAddress.Parse(section.GetComponentAddress().Value)
  round trips in nine configuration test files. The already validated address is
  passed directly. Removed unused address imports from six component registrars.
- Corrected the mapping-context comment and component documentation. Retry output
  completion does not acknowledge downstream consumption; Router side publications
  use native broadcast admission. Gate Open/Close are ordinary command inputs.

## Responsibilities retained

| Area | Assessment |
| --- | --- |
| Configuration | Reads sections and per-component DTOs; registers factories and links. No intermediate application-definition model or global link scanner remains. |
| Application | Resolves native keyed services, attaches links and coordinates startup/shutdown. Constructed-component tracking supports partial-build cleanup; it is not another lookup registry. |
| Core | Defines envelopes, values, optional FlowNode helpers, request/reply and instrumentation. No DI, configuration, application addressing or resilience package dependency. |
| Typed configuration values | PortReference, OneOrManyPortReferences, ExpressionSource and JsonSource encapsulate genuinely reusable parsing rules. Retain them. |
| Lifetime helpers | Processing, independent production and application fan-in have different completion rules. Keep their focused internal helpers; merging them would add branching to FlowNode. |
| Request/reply | PendingRequests tracks active correlations/deadlines. Native DI cannot replace runtime request coordination or its synchronization. |
| Events | ComponentEventBridge adds application address headers while Core remains address-independent. Keep the boundary and failure cleanup. |

No public API, component behavior contract, new abstraction or package boundary was
introduced. Invalid mixed reference shapes and ignored resource scalars now fail
registration rather than silently changing the intended configuration.

## Remaining candidates

The top-level DTO binder intentionally reads declared properties while Router reads
its dynamic route properties separately. It therefore does not reject every unknown
top-level setting. Stronger typo detection would need to recognize registrar-owned
dynamic fields; a global scanner or new metadata schema is not justified for that.

Current documentation still includes many migration narratives and review links.
Consolidating the reading path is useful editorial work; historical reports should
not be treated as current runtime contracts. No further large production-class
deletion is supported by this review.

## Validation

- Six new regression cases per target cover output declaration, local/root resource
  validation and ambiguous single/object/array port references.
- Full Release suite: 2,114 passing test executions on .NET 8 and .NET 10, zero warnings.
- Isolated Core, Application and Configuration package consumers pass on both targets.
- Existing public API baseline passes unchanged.
- No commit or publication.
