# FluxFlow.Expressions.Jsonata

Optional JSONata implementation of the engine-free `FluxFlow.Mapping.IFlowExpressionEngine` contract.

Reference this package only in hosts that choose JSONata. Generic mapping components and the FluxFlow engine do not depend on it.

The optional constructor factory creates a fresh native `EvaluationEnvironment`
per call. Configure native bindings, functions and evaluation limits there. Do not
return shared mutable environments; bound functions must be thread-safe. Runtime
variables and link parameters are available as root properties and `$name`
bindings, overriding configured values with the same names for that call only.

Each invocation serializes its variables once. Root properties and named bindings
start from that same snapshot, so getters are not evaluated again just to create
the native bindings. Native binding trees remain independently owned: a host
function that mutates a binding cannot change the root, a sibling binding or the
next invocation. This conversion is local to expression evaluation, not a clone
of workflow messages during fanout.
