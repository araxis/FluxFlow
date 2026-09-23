# Cycle detection only — 2026-09-22

FlowDependencyOrder.TryCreate computed sorted topological layers, but its sole
production caller only checked for a null result to reject cycles. Replaced it with
internal FlowCycleDetection.HasCycle. It uses the existing incoming-edge counts and
deduplicated adjacency sets, with a single queue of ready nodes. No layer arrays,
per-layer lists or sorting remain. The algorithm stays iterative and processes
nodes/edges in O(V + E) time without defining an execution-order contract.

Application.Validate calls the check directly; removed its private GetDependencyOrder
forwarder. The exception for unsupported business cycles is unchanged. BusinessLinks
still excludes ports identified by native TerminalInputs identity; feedback to control
or reply inputs is not treated as a business cycle. No routing, startup/shutdown,
component or public API change was made.

The eight existing algorithm cases now assert cycle behavior rather than unused
layer ordering. They cover acyclic joins with duplicate edges and an isolated node,
empty graphs, self-loops, connected/disconnected cycles, unknown endpoints and duplicate
node identities. Existing application tests still reject cycles before sources start,
and DirectContractApplicationTests exercises a source -> worker -> source terminal
path through composition, execution and draining.

Validation: all **1,764 Release executions passed** across the solution's existing
targets. The public API baseline passed unchanged. Evidence:
artifacts/cleanup-review-20260920/cycle-detection-tests.log.

Updated Core README terminology and replaced its stale final runtime-revisions
section, which contradicted the current minimum boundary. Historical audit findings
remain in their dated reports; this report completes the cycle-detection follow-up.
No additional production layer, package dependency, commit or publication was added.
