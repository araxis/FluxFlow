# FluxFlow.Components.Timers

Standalone typed timer sources and transforms.

| Node | Contract |
|------|----------|
| `TimerIntervalNode` | source of `TimerIntervalTick` |
| `TimerScheduleNode` | source of `TimerScheduleTick` |
| `TimerDelayNode<T>` | T -> delayed T |
| `TimerThrottleNode<T>` | T -> throttled T |
| `TimerDebounceNode<T>` | T -> debounced T |

Non-generic transform names are explicit `JsonElement` specializations for
configuration workflows. Incoming errors retain lineage and pass through
without delay logic. Timing and completion races are serialized so a claimed
pending value is emitted at most once and never after Output completes.

Nodes use an optional host-owned `TimeProvider`, expose Events, and do not
require Engine or Composition.

## Composition

Install `FluxFlow.Components.Timers.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
