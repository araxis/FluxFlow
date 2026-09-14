# Storage delegation comparison

Decision: retain the existing family-local pipeline. Direct inheritance removes
forwarding members but does not meet the agreed requirement to avoid duplicated
parsing or a new generic base class.

The full direct candidate is in StorageGetNode.direct.cs.txt. It is a review-only
prototype, excluded from compilation. It has not been built or behavior-tested;
production code was not replaced.

| Aspect | Current StorageGetNode | Direct FlowNode candidate |
| --- | --- | --- |
| Node source lines | 137 | 161 |
| Parsing/rejection/capacity setup | Shared 80-line Storage pipeline | Copied into Get |
| Ports/lifetime | Eight forwarding members | Inherited from FlowNode |
| Operation | Original envelope plus typed request | Same |
| Application usage | Construct and add component | Same |
| Public base/interface | IFlowComponent implementation | FlowNode subclass |

Physical line counts include blank lines and comments, excluding trailing empty
lines. The shared pipeline still serves Put, Query and Delete, so migrating Get
alone adds 24 production lines and does not remove that pipeline file. Line count
is secondary: the important regression is a second place to maintain parsing,
rejection details/events and capacity setup. Migrating all four would repeat that
code across the family. Extracting it again would recreate a helper/base layer.

There are also observable changes requiring deliberate treatment:

- FlowTracing tags the executing object's GetType().FullName. Direct inheritance
  changes flow.component.type from the family pipeline to StorageGetNode.
- FlowNode construction precedes the component constructor body, changing which
  validation occurs first and when runtime blocks are allocated on invalid input.
- The public base class changes even though callers can still construct/add the
  component with the same syntax. This is not merely removal of private forwarding.

The candidate uses the existing FlowNode execution and preserves the operation
body, but no runtime/lifetime equivalence is claimed without testing. Since it
already fails the simplicity criterion at source-review level, adopting it and
running a migration would spend effort on a design we do not recommend.

The existing pipeline is small, local to Storage and has one input type parameter.
It shares a real responsibility. Keep it rather than introduce a new public generic
base to hide eight straightforward forwarding members. This conclusion applies to
this concrete comparison, not to every possible component design.

Next useful phase-1 work: audit component-facing documentation and examples against
the simplified canonical APIs, and then run integrated validation across the changed
packages. Several README examples inspected during this sequence still described
typed transport envelopes; correcting the development path has practical value.
