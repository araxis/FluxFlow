# Workflow conversations

Open `Code/FluxFlow.sln` and expand `src/Authoring`.

- `FluxFlow.Conversations`: conversation state, approval modes, cancellation,
  revision checks and provider contracts. No Razor or provider SDK dependency.
- `FluxFlow.Conversations.Local`: the local connection, Microsoft chat-client
  abstraction and editing tool declarations. Register with `AddLocalConversations()`.
- `src/Studio/FluxFlow.Studio.UI/Features/Chat`: shared chat controls and
  `StudioConversationWorkspace`, the adapter to the existing canvas editing APIs.
- `src/Studio/FluxFlow.DesignerApp/Services`: browser HTTP transport. The web host maps endpoints;
  it does not own the conversation or execute editing tools.

`ConversationSession` accepts an `IConversationWorkspace` and `IProposalGenerator`.
The workspace supplies snapshots, prepared editing batches and revision-checked
application. Studio delegates these operations to `DesignerGraphState`; a headless
host can implement the same boundary using `DesignerConfiguration.PrepareEdits`.
All calls for one session must run on the host's editing dispatcher. Do not share
a conversation session between windows or clients.

Each message captures the active workflow, connection and approval mode. A tab
switch cannot redirect a pending request. Every next step receives current
configuration and completed tool outcomes. Manual edits, including Undo followed
by Redo, invalidate an outstanding response or preview. There is no automatic merge.

`SendAsync` accepts optional `selectedComponents` addresses in the active workflow.
The distinct addresses are copied into the turn and each `ProposalRequest`, independent
of later canvas selection or changes to the caller's list. The local provider resolves them through workflow inspection against each
request's configuration snapshot. It supplies component properties without changing
their names or applying defaults. Missing or other-workflow attachments are rejected.

`selectedWorkflows` adds workflow names as reference context. Names are deduplicated,
validated and copied at Send alongside the component addresses. The local provider
uses `InspectWorkflow` to supply their components, known incoming/outgoing links and
inspection diagnostics. Missing workflows are rejected. `Workflow` remains the active
editing target captured at Send; attaching workflows does not change it. Both lists
are retained across steps, while each step receives a fresh configuration snapshot.

`attachments` accepts `TextAttachment` records containing a file name and text only,
without paths or host handles. Send copies the list into the turn and each request.
The same contents accompany every step; later requests do not inherit file contents.
Shared validation accepts `.json` and `.txt` names, at most four files, 32 KB per file
and 64 KB total in UTF-8. Contents are preserved as reference data without parsing or
normalizing JSON. The local provider validates these limits again before transport.

`images` accepts immutable `ImageAttachment` records with a name, PNG/JPEG media type
and base64 content. Up to two images of 2 MB each are captured at Send and retained
for that turn's editing steps. File names, format signatures and sizes are checked
before transport. The local provider checks the selected model's reported vision
capability before sending images through the existing chat SDK. Unsupported models
produce an error; nothing is downloaded or silently omitted. Cancellation during
the capability check or response prevents late results from preparing changes.

Ask mode pauses before each editing batch. Automatic mode applies valid batches
without another prompt. In Studio, each batch is one existing Undo/Redo action.
Other hosts own their history through the workspace adapter. Cancelling or rejecting
a step stops continuation; completed batches remain applied.
Automatic permission covers editing only, never execution, files or installations.
Text answers have no editing side effects. A request stops after 16 batches, or
after a batch produces no changes, to prevent unbounded continuation.

The optional text callback on `IProposalGenerator.GenerateAsync` delivers partial
response text. It is display-only: only the completed result can prepare an edit.
Cancellation ignores later callbacks and results, and revision checks still run
before preparing or applying the completed batch. Providers without streaming can
keep implementing the original overload.

The chat adapter retains the provider's default reasoning mode so reasoning and
answer content remain separate. Only the SDK's answer text is forwarded, both
during streaming and in the final reply. Reasoning is not shown as an answer, and
literal text or code is not rewritten by removing tags.

The local provider uses a standard tool call to request an `edit_workflow` batch
containing the existing typed add/configure/connect operations. Tool results are
sent back only after the host applies them. The provider never invokes these tools
directly. Component discovery and workflow inspection reuse the registered
descriptions and headless editing APIs. These boundaries can also serve a future
tool host without loading Studio; no additional transport is implemented here.

A workspace may supply optional tool descriptions and prepare their calls through
the same revision-checked approval boundary. The default is no additional tools.
Studio supplies its component color tool in `StudioConversationWorkspace`; color
validation, project appearance and history remain in Studio. The provider returns
calls for approval and never executes them. Workflow configuration edits and host
tools run as separate steps, each with its own Undo/Redo action.

The existing Studio tests cover both .NET 8 and 10, with simulated provider
responses and shared UI checks. Live local-service trials are separate.
