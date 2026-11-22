# Configuration authoring

These libraries belong to FluxFlow authoring, independently of Studio. Disk and
Visual Studio folders match under `Code/src/Authoring` in `Code/FluxFlow.sln`.

- **FluxFlow.Authoring** owns configuration documents, editing operations,
  registration validation, component descriptions, workflow inspection and expression
  evaluation. It contains no Razor, canvas state, file dialogs or settings storage.
- **FluxFlow.Conversations** owns the conversation loop, prepared edits, approval,
  cancellation and revision checks. It depends on Authoring, not on Studio or a provider SDK.
- **FluxFlow.Conversations.Local** adapts the local provider to the shared contracts
  using the standard Microsoft chat-client interface and tool calls.

An external command-line application or MCP host can reference **Authoring alone**
to discover components, inspect configuration and prepare edits. It does not need
a conversation, local provider or Studio. A host that also runs conversations adds
Conversations and supplies `IConversationWorkspace` plus `IProposalGenerator`.

Hosts own document persistence, write-conflict checks and their editing dispatcher.
The authoring libraries do not activate runtime components or write project files.
Component descriptions are shared metadata: properties, defaults, ports and display
hints. Both discovery tools and Studio consume the same descriptions through DI;
there is no second registration list or rendering dependency.

Studio keeps the shared Razor chat panel and `StudioConversationWorkspace` adapter
under `src/Studio/FluxFlow.Studio.UI/Features/Chat`. The adapter delegates accepted
batches to the existing canvas Undo/Redo API. The web host only provides transport.

Dependency direction:

```text
command-line or tool host -> Authoring -> Configuration
Studio UI -> Conversations -> Authoring
local provider adapter -> Conversations
```

See [configuration editing and discovery](FluxFlow.Authoring/README.md) and
[conversation behavior](FluxFlow.Conversations/README.md) for the existing APIs.
No command-line or MCP transport is introduced by this separation.
