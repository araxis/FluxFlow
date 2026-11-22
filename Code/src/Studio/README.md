# FluxFlow Studio

Studio belongs to the shared `Code/FluxFlow.sln` solution. Open that solution in
Visual Studio; its `src/Studio` folder matches this directory.

- **FluxFlow.Studio.UI**: shared Razor class library with the Studio page, canvas,
  Inspector, editors, docking and styles on .NET 8, 10 and 11.
- **FluxFlow.DesignerApp**: thin WebAssembly entry point with browser file access,
  local settings storage and HTTP adapters for expression evaluation and proposals.
- **FluxFlow.StudioWebHost**: thin ASP.NET Core host serving the published UI and
  expression evaluation and proposal endpoints.
- **tests/Unit/FluxFlow.Studio.Tests**: configuration preservation, editing,
  layout and evaluation checks on .NET 8, 10 and 11.

Studio references the engine projects directly. It needs no local NuGet feed or
separate solution. Engine packages do not depend on Studio.

Reusable configuration editing, descriptions, validation and conversations live in
[src/Authoring](../Authoring/README.md). Studio consumes these libraries; they do not
depend on Studio. Only chat rendering and the canvas workspace adapter live here.

From `Code`, run:

```powershell
./eng/run-studio.ps1
```

This builds the Studio distribution in `Code/artifacts/studio-distribution/<framework>` and starts the web
host at `http://127.0.0.1:5289`. The default host and browser use .NET 11.
Pass `-Framework net8.0` or `-Framework net10.0` to use an older companion;
the browser still builds on .NET 11. Browser publish intermediates are isolated by
target framework so an SDK upgrade cannot reuse the previous framework's WebCIL assets.
The [Windows desktop host](FluxFlow.Studio.Desktop/README.md) renders the same components
and supplies native file access, settings storage and execution through DI.

Contract v1 plugins retain their .NET 8 and .NET 10 payloads. .NET 11 hosts use
the compatible .NET 10 runtime and portable design assemblies; installing the new
host does not require rebuilding plugin packages or converting project files.

The distribution includes five ordinary packages: Sources, Logging, Routing, Transformation and FlowControl.
The first companion start with a fresh plugin store installs them together. Removing a package in Plugins
is permanent across restarts unless you reinstall it. Existing stores are not seeded. Device and protocol
families are separate installs. To build the distribution without starting it, use `./eng/publish-studio.ps1`;
publishing only the companion project produces a bare host.

## Local proposals

Open **Edit → Studio settings → Connection** to select an installed model and
test it. Endpoint and model changes autosave in Studio preferences, outside project
files and Undo/Redo. Test connection sends a small request to the selected model;
refreshing the list does not generate text or download models.

Until a connection is saved, `Proposals__Model` and `Proposals__Endpoint` supply host
defaults (endpoint `http://127.0.0.1:11434/`). The service must support `GET /api/tags`
and `POST /api/chat` with messages and editing tool calls. The host requires
a loopback endpoint; browser code never connects to the service directly. The
endpoint refers to the Studio host machine, not a remote browser's computer.

Use **Open chat** in the toolbar or **View → Show chat** to request workflow edits.
The shared Chat panel contains answers, editing progress and approval controls;
Observations remains for output, events, logs and validation. Ask mode requires
Apply/Discard for each batch; Automatic applies editing batches immediately.
Configuration details remain expandable. Each applied batch is one Undo/Redo action.
Each request captures the active workflow; navigation cannot redirect pending work.
Select components on the canvas to see dashed suggestion chips above the message.
Click a suggestion's **+** chip to attach it. It becomes a regular chip with a remove button.
Suggestions are not sent until attached. You can attach several components; duplicates are ignored.
Remove any chip before sending to exclude that component. Sending captures their addresses
and current configuration; selecting other components afterwards does not retarget the request.
The sent message retains the attachment labels. Unsent component attachments survive property
edits, but clear on workflow or project navigation, or when their address disappears
after renaming, deletion or Undo.
The active workflow also appears as a dashed **Workflow: name** chip. Attach it,
switch tabs and attach another to include several workflows. Workflow attachments
survive tab navigation; remove a chip to exclude it. They clear when the workflow
disappears or the project/chat is reset. The active tab at Send remains the default
editing target, shown in the chat header. Attached workflows provide reference
context without changing that target. Inspection includes known cross-workflow
links and reports incomplete inspection for components without descriptions.
Use the paperclip to attach `.json` or `.txt` sample files, one at a time: up to
four files, 32 KB per file and 64 KB total (UTF-8). Each appears as a removable chip;
choosing the same file name replaces its draft attachment. Contents are read when
chosen and captured unchanged at Send, including whitespace and property casing.
JSON is treated as sample text, so incomplete JSON can also be discussed. Files are
reference data, not instructions. They stay with that message and its editing steps;
reattach them for another message. Chat/project reset clears draft attachments.
Attachments do not edit configuration or create history entries.
The host supplies a fresh snapshot and DI descriptions for every step. Responses
are bounded to 32 edits, with a two-minute deadline per step and at most 16 batches
per request. Cancellation or failure stops further changes; earlier applied batches
remain undoable. Conversation never executes a workflow or saves project files.
Automated tests use simulated responses and do not require the local service.

Responses stream through `/api/proposals/stream` as newline-delimited JSON. The
shared panel formats Markdown lists, code and tables; raw HTML and remote images
cannot execute or load through response text. Stop cancels the current request.
Partial responses never apply edits. Chat can change or reset a component's color,
for example "Make main.source blue." Ask previews the component address and color;
Automatic uses the same validated action without a separate approval. Each change
is one Undo/Redo action. Color belongs to the Studio project, not exported workflow
configuration. The target workflow is captured at Send, and manual edits invalidate
pending changes. Other canvas presentation tools remain deferred.

### Dictation

Studio settings has a Dictation category with enable/disable, Local/Remote, language
(Auto, Persian or English) and a transcription model identifier. It autosaves
separately from project configuration. Existing settings default to Auto.
Dictation is disabled by default. Start the microphone, then finish to append editable
text to the current draft; cancel discards the recording. Messages are never sent
automatically. Capture stops after one minute. Changing workflows, clearing chat,
changing dictation settings or closing the panel cancels pending dictation.

The web host accepts recorded audio at `/api/dictation/transcribe`. Configure the
full provider transcription URL with `Dictation:Local:Endpoint` (loopback HTTP/HTTPS)
or `Dictation:Remote:Endpoint` (HTTPS). Each endpoint accepts multipart `file`, `model`
and `response_format=json`, and returns `{ "text": "..." }`. Explicit language choices
send `language=fa` or `language=en`. Auto sends `language=auto` to Local and omits
the language field for Remote. Optional `ApiKey` under
the same section supplies a Bearer credential; keep credentials in host environment
variables or a secret store. Optional `Model` restricts a service that loads one
fixed model to its configured identifier. Environment keys use double underscores,
for example `Dictation__Local__Endpoint`. There are no automatic downloads, redirects
or fallback between Local and Remote. Local refers to the Studio host machine.

The browser adapter records with MediaRecorder and converts to mono WAV using Web
Audio. Desktop hosts can register `IDictationService` with native capture and either
in-process transcription or a service. The shared composer has no browser or native
audio dependency. Dictation never changes workflow configuration or its undo history.
Browser capture tests run with `node --test tests/Unit/FluxFlow.Studio.Tests/BrowserDictationTests.mjs`
from the `Code` directory; .NET tests use simulated capture and transcription.

See [the UI guide](FluxFlow.Studio.UI/README.md) for host registration, project files
and settings.

### Execution observations

Studio's Logs, Traces and Output panels display the current run's observations.
The companion retains the OpenTelemetry SDK's OTLP logs and traces without an
external telemetry service. Runner diagnostics remain separate from original
engine logs. Each run has its own capture; late records are rejected after Stop,
and the captured records remain available for inspection. Starting another run
creates a new capture. Viewing observations does not edit project configuration
or its Undo/Redo history.
