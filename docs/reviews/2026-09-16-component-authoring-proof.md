# Component authoring proof with the current bases

The package-only consumer now includes NumberRouter: one component class derived
from FlowNode, with the standard output and a named Negative output. Its constructor
creates the additional owned output; RegisterPorts names it; ProcessAsync chooses
the destination. No component-local exception catcher, tracing setup, completion
pump or disposal implementation is required.

The executable proof sends typed NumberValue payloads through both addressed
outputs, preserves payload reference identity without constructing JSON, checks
that an invalid local payload becomes error data, then checks recovery on the next
message. Application shutdown must complete the additional output. Existing
package checks still exercise ordered-transform construction, a started source,
direct native-block registration, JSON construction and concurrent request/reply.

The class is a consumer fixture, not a restored production component package.
See eng/core-consumer/Program.cs for the complete executable implementation.

## Base-class review

| Current wrapper | Native behavior retained |
| --- | --- |
| FlowNode | ActionBlock execution, zero or many emissions, additional outputs, final flush |
| FlowTransformNode | TransformBlock one-result processing with ordered parallel results |
| FlowSource | Explicit production startup and separate production/control shutdown phases |

The processing wrappers already share lifetime ownership; all three share required
output acceptance. A common ownership superclass alone would not remove the
different authoring methods. Replacing ordered transforms with ActionBlock emission
would change ordering. A mode flag or processor-factory constructor would introduce
another configuration mechanism for developers to learn.

Recommendation from this review: retain the three wrappers for now, rather than
merge solely to reduce the class count. This does not prove that a simpler common
base is impossible, nor complete the planned common-base proof. A future replacement
must show simpler complete component code and preserve the listed behaviors.
No new framework abstraction or runtime implementation was added in this step.

Evidence lives under artifacts/component-proof-20260916/. The initial consumer
attempt referenced an internal endpoint property and failed compilation; the
corrected fixture uses only public APIs. No commit, push or publication.

Final package consumer qualification passed on .NET 8.0.31 and .NET 10.0.12,
using a fresh isolated Core-only package feed (qualified-consumer/). No production
code changed, so the full 2,379-test solution suite was not repeated for this
fixture/documentation step. The previous regression result remains historical.
