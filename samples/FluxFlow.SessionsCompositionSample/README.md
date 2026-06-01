# FluxFlow Sessions Composition Sample

This sample shows how a host can compose `FluxFlow.Components.Sessions`
without putting storage inside the component package.

The sample runs two workflows against the same host-owned in-memory store:

1. `sample.session-input -> session.recorder -> sample.session-sink`
2. `session.replay -> sample.session-sink`

The first workflow records three neutral `SessionRecordInput` messages. The
second workflow replays the stored session as `SessionRecord` output.

Run it with:

```powershell
dotnet run --project samples\FluxFlow.SessionsCompositionSample\FluxFlow.SessionsCompositionSample.csproj
```
