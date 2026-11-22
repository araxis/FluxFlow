# Conditional-link compilation through DI

LinkConditionCompiler duplicated a service lookup: its only caller selected a compiled
condition by engine and expression, and its dictionary retained one Lazy delegate per
provider. Link registration now uses native TryAddKeyedSingleton with the exact
engine/expression tuple and Lazy<Func<FlowMapContext, bool>> as the service type.
The separate compiler class and dictionary are deleted.

The short compilation method remains private to LinkRegistrationExtensions. It resolves
the same explicit or default expression engine and passes the existing input, payload
and message type hints. No expression-engine API or component implementation changes.

Lazy remains necessary because DI retries service factories that throw. Registering
the Lazy successfully before evaluating it retains compilation exceptions as well as
successful results, preserving the prior contract. Registration does not construct
an engine or compile an expression. Native DI gives each provider its own Lazy instance.
Unconditional links have no compilation registration. Message evaluation is never cached.

Link identity remains separate from compilation identity: source, target, condition,
engine and error selection identify links; engine and condition alone identify compiled
expressions. Normal and error links can therefore share compilation while keeping their
different filters and destinations. Explicit engine names never fall back to the default.

Regression coverage exercises successful and failed compilation across normal/error
links, rethrows the same exception without recompiling, verifies provider isolation,
and keeps identical expression text separate across default and two explicit engines.
Existing real-engine delivery, custom type hints and both declaration directions remain.

No new public type, API, configuration syntax, project or compatibility layer is added.

Validation: Configuration passed 293 tests per .NET 8/10 target. The full Release suite
passed 2,056 test executions across 28 projects with zero warnings, including the unchanged
public API baseline. The isolated Configuration package consumer passed .NET 8.0.31 and
10.0.12. Source search confirms the old compiler class and lookup are absent; diff checks pass.

No commit or publication is part of this change.
